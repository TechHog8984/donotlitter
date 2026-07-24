#include <stdarg.h>
#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <dlfcn.h>
#include <pthread.h>

typedef int (*coreclr_initialize_fn)(
    const char *exePath, const char *appDomainFriendlyName,
    int propertyCount, const char **propertyKeys, const char **propertyValues,
    void **hostHandle, unsigned int *domainId);

typedef int (*coreclr_create_delegate_fn)(
    void *hostHandle, unsigned int domainId,
    const char *entryPointAssemblyName, const char *entryPointTypeName,
    const char *entryPointMethodName, void **delegate);

static coreclr_initialize_fn real_coreclr_initialize = NULL;
static coreclr_create_delegate_fn p_create_delegate = NULL;
static void *captured_host_handle = NULL;
static unsigned int captured_domain_id = 0;

static void log_msg(const char *fmt, ...) {
    va_list a; va_start(a, fmt);
    fprintf(stderr, "[donotlitter] "); vfprintf(stderr, fmt, a); fprintf(stderr, "\n");
    va_end(a);
}

static int checkenv(const char* value) {
    return value && value[0] != 0;
}

// ---- bootstrap the REAL dlsym (avoid infinite recursion into ourselves) ----
typedef void *(*dlsym_fn)(void *, const char *);
static dlsym_fn real_dlsym = NULL;
static void ensure_real_dlsym(void) {
    if (real_dlsym) return;
    real_dlsym = (dlsym_fn) dlvsym(RTLD_NEXT, "dlsym", "GLIBC_2.2.5");
    if (!real_dlsym) real_dlsym = (dlsym_fn) dlsym(RTLD_NEXT, "dlsym");
}

static void *load_mod_thread(void *arg);

static int hooked_coreclr_initialize(
    const char *exePath, const char *appDomainFriendlyName,
    int propertyCount, const char **propertyKeys, const char **propertyValues,
    void **hostHandle, unsigned int *domainId)
{
    log_msg("coreclr_initialize intercepted (exePath=%s)", exePath);

    // Augment TRUSTED_PLATFORM_ASSEMBLIES with our mod DLL so
    // coreclr_create_delegate can find it later by simple name.
    const char *mod_dll = getenv("DONOTLITTER_ASSEMBLY");
    if (!checkenv(mod_dll)) {
        log_msg("invalid DONOTLITTER_ASSEMBLY variable '%s'", mod_dll);
        return 0;
    }

    const char **keys = propertyKeys;
    const char **values = propertyValues;
    char *tpa_buf = NULL;

    if (mod_dll) {
        char **newValues = malloc(sizeof(char *) * propertyCount);
        for (int i = 0; i < propertyCount; i++) newValues[i] = (char *)propertyValues[i];
        for (int i = 0; i < propertyCount; i++) {
            if (strcmp(propertyKeys[i], "TRUSTED_PLATFORM_ASSEMBLIES") == 0) {
                size_t len = strlen(propertyValues[i]) + strlen(mod_dll) + 2;
                tpa_buf = malloc(len);
                snprintf(tpa_buf, len, "%s:%s", propertyValues[i], mod_dll);
                newValues[i] = tpa_buf;
                log_msg("appended %s to TPA list", mod_dll);
                break;
            }
        }
        values = (const char **)newValues;
    }

    int rc = real_coreclr_initialize(exePath, appDomainFriendlyName,
        propertyCount, keys, values, hostHandle, domainId);

    if (rc >= 0 && hostHandle && domainId) {
        captured_host_handle = *hostHandle;
        captured_domain_id = *domainId;
        log_msg("captured host_handle=%p domain_id=%u", captured_host_handle, captured_domain_id);
        pthread_t t;
        pthread_create(&t, NULL, load_mod_thread, NULL);
        pthread_detach(t);
    } else {
        log_msg("real coreclr_initialize failed, rc=0x%x", rc);
    }
    return rc;
}

void *dlsym(void *handle, const char *symbol) {
    ensure_real_dlsym();
    if (strcmp(symbol, "coreclr_initialize") == 0) {
        void *real = real_dlsym(handle, symbol);
        if (real && !real_coreclr_initialize) {
            real_coreclr_initialize = (coreclr_initialize_fn) real;
            log_msg("hooked coreclr_initialize, real=%p", real);
        }
        return (void *) hooked_coreclr_initialize;
    }
    return real_dlsym(handle, symbol);
}

static void *load_mod_thread(void *arg) {
    (void)arg;
    const char *libcoreclr_path = getenv("DONOTLITTER_LIBCORECLR_PATH");
    if (!checkenv(libcoreclr_path)) {
        log_msg("invalid DONOTLITTER_LIBCORECLR_PATH variable '%s'", libcoreclr_path);
        return NULL;

    }
    void *libcoreclr = dlopen(libcoreclr_path, RTLD_NOW);
    if (!libcoreclr) { log_msg("dlopen libcoreclr failed: %s", dlerror()); return NULL; }

    p_create_delegate = (coreclr_create_delegate_fn)
        real_dlsym(libcoreclr, "coreclr_create_delegate");
    if (!p_create_delegate) { log_msg("no coreclr_create_delegate"); return NULL; }

    const char *modname = getenv("DONOTLITTER_MOD_NAME");
    if (!checkenv(modname)) {
        log_msg("invalid DONOTLITTER_MOD_NAME variable '%s'", modname);
        return NULL;
    }
    size_t modtypename_size = strlen(modname) + 7;
    char *modtypename = malloc(modtypename_size);
    snprintf(modtypename, modtypename_size, "%s.Entry", modname);

    void *delegate = NULL;
    int rc = p_create_delegate(
        captured_host_handle, captured_domain_id,
        modname,
        modtypename,
        "Init",
        &delegate);

    if (rc < 0 || !delegate) { log_msg("coreclr_create_delegate failed rc=0x%x", rc); return NULL; }

    log_msg("calling %s.Init()", modtypename);
    free(modtypename);
    ((void (*)(void))delegate)();
    log_msg("mod init returned");
    return NULL;
}

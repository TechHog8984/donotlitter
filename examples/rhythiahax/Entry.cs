using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using HarmonyLib;

namespace RhythiaHax
{
    public static class Options
    {
        public static bool noMiss = false;
        public static bool aimbot = false;
    }

    public static class Entry
    {
        public static void Init()
        {
            string modDir = Path.GetDirectoryName(typeof(Entry).Assembly.Location);
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                string candidate = Path.Combine(modDir, name.Name + ".dll");
                return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
            };
            RealInit();
        }

        public static Assembly gameAsm = null;

        public static Type legacyRunnerType = null;
        public static MethodInfo updateCursorMethod = null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RealInit()
        {
            try
            {
                var harmony = new Harmony("com.techhog.rhythiahax");

                for (int i = 0; i < 100; i++)
                {
                    gameAsm = AssemblyLoadContext.All
                        .SelectMany(ctx => ctx.Assemblies)
                        .FirstOrDefault(a => a.GetName().Name == "Rhythia");
                    if (gameAsm != null) break;
                    System.Threading.Thread.Sleep(100);
                }

                if (gameAsm == null)
                {
                    Console.WriteLine("[RhythiaHax] Rhythia never found. All loaded ALCs and assemblies:");
                    foreach (var ctx in AssemblyLoadContext.All)
                    {
                        Console.WriteLine($"  ALC: {ctx.Name}");
                        foreach (var a in ctx.Assemblies)
                            Console.WriteLine("    " + a.GetName().Name);
                    }
                    return;
                }

                Console.WriteLine("[RhythiaHax] GameAsm: " + gameAsm);

                {
                    var targetType = gameAsm.GetType("MainMenu");
                    var targetMethod = targetType.GetMethod("Transition", BindingFlags.Public | BindingFlags.Instance);

                    var postfix = typeof(Patches).GetMethod(nameof(Patches.TransitionPostfix));
                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));

                    Console.WriteLine("[RhythiaHax] patched MainMenu.Transition");
                }

                {
                    var targetType = gameAsm.GetType("MainMenu");
                    var targetMethod = targetType.GetMethod("Load", BindingFlags.Public | BindingFlags.Instance);

                    var postfix = typeof(Patches).GetMethod(nameof(Patches.LoadPostfix));
                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));

                    Console.WriteLine("[RhythiaHax] patched MainMenu.Load");
                }

                legacyRunnerType = gameAsm.GetType("LegacyRunner");
                {
                    updateCursorMethod = legacyRunnerType.GetMethod("UpdateCursor", BindingFlags.Public | BindingFlags.Static);

                    var prefix = typeof(Patches).GetMethod(nameof(Patches.UpdateCursorPrefix));
                    harmony.Patch(updateCursorMethod, prefix: new HarmonyMethod(prefix));

                    Console.WriteLine("[RhythiaHax] patched LegacyRunner.UpdateCursor");
                }

                {
                    var targetMethod = legacyRunnerType.GetMethod("_Process", BindingFlags.Public | BindingFlags.Instance);
                    var transpiler = typeof(Patches).GetMethod(nameof(Patches.ProcessTranspiler));
                    var prefix = typeof(Patches).GetMethod(nameof(Patches.ProcessPrefix));

                    harmony.Patch(targetMethod, transpiler: new HarmonyMethod(transpiler), prefix: new HarmonyMethod(prefix));
                }

                GameSettings.Init();
            }
            catch (Exception e)
            {
                Console.WriteLine("[RhythiaHax] RealInit failed: " + e);
            }
        }

        private static bool HasInitializedUI = false;
        public static ModText NoteDebugText = null;
        public static void InitializeUI()
        {
            if (HasInitializedUI) return;
            HasInitializedUI = true;
            ModUI.Show(10f, 80f);

            ModUI.AddText("RhythiaHax by techhog");

            ModUI.AddToggle("No Miss", enabled => Options.noMiss = enabled);
            ModUI.AddToggle("Aimbot", enabled => Options.aimbot = enabled);

            NoteDebugText = ModUI.AddText("Note Debug");
        }

        public static bool IsPaused()
        {
            // TODO: i think we have to also check for the pause state like if we have space to pause i think it will do that instead of this? idk
            var field = legacyRunnerType.GetField("MenuShown", BindingFlags.Public | BindingFlags.Static);
            return (bool)field.GetValue(null);
        }

        public static object GetCurrentAttempt()
        {
            var field = legacyRunnerType.GetField("CurrentAttempt", BindingFlags.Public | BindingFlags.Static);

            return field.GetValue(null);
        }
        public static bool CurrentAttemptIsReplay()
        {
            var currentAttempt = GetCurrentAttempt();
            var field = currentAttempt.GetType().GetField("IsReplay", BindingFlags.Public | BindingFlags.Instance);

            return (bool)field.GetValue(currentAttempt);
        }
        public static object CurrentAttemptCursorPosition()
        {
            var currentAttempt = GetCurrentAttempt();
            var field = currentAttempt.GetType().GetField("CursorPosition", BindingFlags.Public | BindingFlags.Instance);

            return field.GetValue(currentAttempt);
        }
        public static double CurrentAttemptProgress()
        {
            var currentAttempt = GetCurrentAttempt();
            var field = currentAttempt.GetType().GetField("Progress", BindingFlags.Public | BindingFlags.Instance);

            return (double)field.GetValue(currentAttempt);
        }
        public static uint CurrentAttemptPassedNotes()
        {
            var currentAttempt = GetCurrentAttempt();
            var field = currentAttempt.GetType().GetField("PassedNotes", BindingFlags.Public | BindingFlags.Instance);

            return (uint)field.GetValue(currentAttempt);
        }
        public static object[] CurrentAttemptNotes()
        {
            var currentAttempt = GetCurrentAttempt();
            var mapField = currentAttempt.GetType().GetField("Map", BindingFlags.Public | BindingFlags.Instance);
            var mapValue = mapField.GetValue(currentAttempt);

            var notesField = mapValue.GetType().GetField("notes", BindingFlags.NonPublic | BindingFlags.Instance);
            var notes = notesField.GetValue(mapValue);

            return ((System.Collections.IEnumerable)notes)
                .Cast<object>()
                .ToArray();
        }
        public static int GetNoteMillisecond(object note)
        {
            var method = note.GetType().GetMethod("get_Millisecond", BindingFlags.Public | BindingFlags.Instance);
            return (int)method.Invoke(note, []);
        }
        public static float GetNoteX(object note)
        {
            var method = note.GetType().GetMethod("get_X", BindingFlags.Public | BindingFlags.Instance);
            return (float)method.Invoke(note, []);
        }
        public static float GetNoteY(object note)
        {
            var method = note.GetType().GetMethod("get_Y", BindingFlags.Public | BindingFlags.Instance);
            return (float)method.Invoke(note, []);
        }

        public static class GameSettings
        {
            private static object SettingsInstance;

            private static object SensitivityItem;
            private static MethodInfo SensitivityGet;

            public static void Init()
            {
                var settingsManagerType = gameAsm.GetType("SettingsManager");
                SettingsInstance = settingsManagerType.GetField("Settings", BindingFlags.Public | BindingFlags.Instance)
                    .GetValue(
                        settingsManagerType.GetMethod("get_Instance", BindingFlags.Public | BindingFlags.Static)
                            .Invoke(null, [])
                    );

                var SettingsType = SettingsInstance.GetType();

                SensitivityItem = SettingsType.GetMethod("get_Sensitivity", BindingFlags.Public | BindingFlags.Instance)
                    .Invoke(SettingsInstance, []);
                SensitivityGet = SensitivityItem.GetType().GetMethod("get_Value", BindingFlags.Public | BindingFlags.Instance);
            }

            public static double GetSensitivity()
            {
                return (double)SensitivityGet.Invoke(SensitivityItem, []);
            }
        }
    }

    public static class Patches
    {
        public static void TransitionPostfix(object __instance, object menu)
        {
            var playMenuField = __instance.GetType().GetField("PlayMenu");
            var playMenu = playMenuField.GetValue(__instance);

            if (ReferenceEquals(menu, playMenu))
            {
                // play button pressed
            }
        }

        public static void LoadPostfix(object __instance)
        {
            Entry.InitializeUI();
        }

        // NOTE: false means block

        public static bool UpdateCursorPrefix(object mouseDelta)
        {
            var trace = new StackTrace();
            var callerFrame = trace.GetFrame(2);
            var callerMethod = callerFrame?.GetMethod();

            if (callerMethod == null || callerMethod.DeclaringType == null)
            {
                return true;
            }

            var methodName = callerMethod.Name;

            if (methodName == "_Input")
            {
                return !Options.aimbot;
            }

            return true;
        }

        public static IEnumerable<CodeInstruction> ProcessTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);

            int start = 0;
            int end = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (start == 0 && codes[i].opcode == OpCodes.Callvirt && (codes[i].operand as MethodInfo).Name == "get_X")
                {
                    start = i;
                }
                else if (codes[i].opcode == OpCodes.Call && (codes[i].operand as MethodInfo)?.Name == "Hit")
                {
                    end = i;
                    break;
                }
            }

            Debug.Assert(start > 0 && end > 0);

            var noMissField = AccessTools.Field(typeof(RhythiaHax.Options), nameof(RhythiaHax.Options.noMiss));

            for (int i = start; i < end; i++)
            {
                if (codes[i].opcode == OpCodes.Bgt_Un || codes[i].opcode == OpCodes.Blt_Un)
                {
                    var original = codes[i];

                    var noMissLabel = generator.DefineLabel(); // where we go if noMiss == true
                    var afterLabel = generator.DefineLabel();  // rejoin point after either path

                    var ldsfld = new CodeInstruction(OpCodes.Ldsfld, noMissField);
                    ldsfld.labels.AddRange(original.labels); // preserve incoming branch targets

                    var brtrue = new CodeInstruction(OpCodes.Brtrue, noMissLabel);

                    // keep the original branch, same opcode + target, just re-wrapped
                    var originalBranch = new CodeInstruction(original.opcode, original.operand);

                    var brAfter = new CodeInstruction(OpCodes.Br, afterLabel);

                    var pop1 = new CodeInstruction(OpCodes.Pop);
                    pop1.labels.Add(noMissLabel);
                    var pop2 = new CodeInstruction(OpCodes.Pop);

                    var replacement = new List<CodeInstruction>
                        {
                            ldsfld,        // push Options.noMiss
                            brtrue,        // if true -> jump to noMissLabel (pop both, fall through, branch never taken)
                            originalBranch,// if false -> normal bgt.un/blt.un behavior, consumes same 2 stack values
                            brAfter,       // after taking the normal "not jumped" path, skip past the pops below
                            pop1,          // noMissLabel: clear the two values
                            pop2
                        };

                    codes.RemoveAt(i);
                    codes.InsertRange(i, replacement);

                    // whatever instruction now follows the block becomes the rejoin point
                    int afterIndex = i + replacement.Count;
                    if (afterIndex < codes.Count)
                        codes[afterIndex].labels.Add(afterLabel);

                    int inc = replacement.Count - 1;
                    i += inc; // loop's i++ lands past the inserted block
                    end += inc;
                }
            }

            return codes;
        }
        public static bool ProcessPrefix(object __instance, double delta)
        {
            if (!Entry.CurrentAttemptIsReplay() && Options.aimbot)
            {
                float x = 0f;
                float y = 0f;

                if (!Entry.IsPaused())
                {
                    var notes = Entry.CurrentAttemptNotes();
                    var passedNotes = Entry.CurrentAttemptPassedNotes();
                    if (passedNotes < notes.Length)
                    {
                        var firstNote = notes[passedNotes];
                        var secondNote = firstNote;
                        if (passedNotes + 1 < notes.Length)
                            secondNote = notes[passedNotes + 1];

                        var firstNoteX = Entry.GetNoteX(firstNote);
                        var firstNoteY = Entry.GetNoteY(firstNote);

                        var secondNoteX = Entry.GetNoteX(secondNote);
                        var secondNoteY = Entry.GetNoteY(secondNote);

                        float dist = Single.Hypot(firstNoteX - secondNoteX, firstNoteY - secondNoteY);
                        bool adjacent = dist <= 1f;

                        float targetX = firstNoteX;
                        float targetY = firstNoteY;

                        if (adjacent)
                        {
                            targetX = (firstNoteX + secondNoteX) * 0.5f;
                            targetY = (firstNoteY + secondNoteY) * 0.5f;
                        }
                        else
                        {
                            targetX = firstNoteX + (secondNoteX - firstNoteX) * 0.15f;
                            targetY = firstNoteY + (secondNoteY - firstNoteY) * 0.15f;
                        }

                        var progress = Entry.CurrentAttemptProgress();
                        var firstNoteMS = Entry.GetNoteMillisecond(firstNote);
                        var secondNoteMS = Entry.GetNoteMillisecond(secondNote);

                        var cursor = Entry.CurrentAttemptCursorPosition();
                        var cursorY = (float)cursor.GetType().GetField("Y", BindingFlags.Public | BindingFlags.Instance).GetValue(cursor);
                        var cursorX = (float)cursor.GetType().GetField("X", BindingFlags.Public | BindingFlags.Instance).GetValue(cursor);

                        // TODO: account for absoluteinput and cursordrift
                        var multiplier = 30f / (float)Entry.GameSettings.GetSensitivity();
                        x = -(cursorX - targetX) * multiplier;
                        y = (cursorY - targetY) * multiplier;

                        Entry.NoteDebugText.Update("Cursor: (" + cursorX + ", " + cursorY + ")\nTarget: (" + targetX + ", " + targetY + ")\nXY: " + x + ", " + y);
                    }
                }

                Entry.updateCursorMethod.Invoke(__instance, [GodotReflect.Vector2(x, y)]);
            }

            return true;
        }
    }

    // Handle returned by AddText so you can update it later
    public class ModText
    {
        private readonly object label;

        internal ModText(object label)
        {
            this.label = label;
        }

        public void Update(string text)
        {
            GodotReflect.Set(label, "Text", text);
        }
    }

    public static class ModUI
    {
        private static object panel;
        private static object vbox;
        private static bool shown = false;

        private static float sizey = 40f;

        public static void Show(float posx, float posy)
        {
            if (shown) return;
            shown = true;

            object canvasLayer = GodotReflect.New("Godot.CanvasLayer");

            panel = GodotReflect.New("Godot.Panel");
            var vector2Type = GodotReflect.T("Godot.Vector2");

            GodotReflect.Set(panel, "Position", Activator.CreateInstance(vector2Type, posx, posy));
            UpdateSize();

            // layout for children
            vbox = GodotReflect.New("Godot.VBoxContainer");
            GodotReflect.Set(vbox, "Position", Activator.CreateInstance(vector2Type, 10f, 10f));
            GodotReflect.Call(panel, "AddChild", vbox);

            GodotReflect.Call(canvasLayer, "AddChild", panel);

            object mainLoop = GodotReflect.CallStatic("Godot.Engine", "GetMainLoop");
            object root = GodotReflect.Get(mainLoop, "Root");
            GodotReflect.Call(root, "AddChild", canvasLayer);
        }

        private static void UpdateSize()
        {
            GodotReflect.Set(panel, "Size", GodotReflect.Vector2(240f, sizey));
        }

        public static ModText AddText(string initialText)
        {
            object label = GodotReflect.New("Godot.Label");
            GodotReflect.Set(label, "Text", initialText);
            GodotReflect.Call(vbox, "AddChild", label);
            sizey += new GodotReflect.Vector2Wrapper(GodotReflect.Get(label, "Size")).GetY();
            UpdateSize();

            return new ModText(label);
        }

        public static void AddToggle(string name, Action<bool> onPress)
        {
            object checkbox = GodotReflect.New("Godot.CheckBox");
            GodotReflect.Set(checkbox, "Text", name);
            GodotReflect.Call(vbox, "AddChild", checkbox);
            sizey += new GodotReflect.Vector2Wrapper(GodotReflect.Get(checkbox, "Size")).GetY();
            UpdateSize();

            var callableType = GodotReflect.T("Godot.Callable");
            var fromMethod = System.Linq.Enumerable.First(
                callableType.GetMethods(BindingFlags.Public | BindingFlags.Static),
                m => m.Name == "From"
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.Name == "Action`1");

            var boundFrom = fromMethod.MakeGenericMethod(typeof(bool));
            object callable = boundFrom.Invoke(null, new object[] { onPress });

            object signalName = GodotReflect.StringName("toggled");
            GodotReflect.Call(checkbox, "Connect", signalName, callable);
        }
    }

    public static class GodotReflect
    {
        private static Assembly godotAsm;

        public static Assembly Asm => godotAsm ??= System.Runtime.Loader.AssemblyLoadContext.All
            .SelectMany(ctx => ctx.Assemblies)
            .First(a => a.GetName().Name == "GodotSharp");

        public static Type T(string name) => Asm.GetType(name);

        public static object New(string typeName, params object[] args)
            => Activator.CreateInstance(T(typeName), args);

        public static object Call(object target, string method, params object[] args)
        {
            var methodInfo = target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            var parameters = methodInfo.GetParameters();

            if (args.Length < parameters.Length)
            {
                var padded = new object[parameters.Length];
                Array.Copy(args, padded, args.Length);
                for (int i = args.Length; i < parameters.Length; i++)
                    padded[i] = Type.Missing;
                args = padded;
            }

            return methodInfo.Invoke(target, args);
        }

        public static object CallStatic(string typeName, string method, params object[] args)
            => T(typeName).GetMethod(method, BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, args);

        public static void Set(object target, string prop, object value)
            => target.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)
                .SetValue(target, value);

        public static object Get(object target, string prop)
            => target.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)
                .GetValue(target);

        public class Vector2Wrapper
        {
            private object obj;
            private static Type type = T("Godot.Vector2");
            private static FieldInfo fieldx = type.GetField("X", BindingFlags.Public | BindingFlags.Instance);
            private static FieldInfo fieldy = type.GetField("Y", BindingFlags.Public | BindingFlags.Instance);

            public Vector2Wrapper(object obj)
            {
                this.obj = obj;
            }

            public float GetX()
            {
                return (float)fieldx.GetValue(obj);
            }
            public float GetY()
            {
                return (float)fieldy.GetValue(obj);
            }
        }

        public static object StringName(string s)
            => Activator.CreateInstance(T("Godot.StringName"), s);

        public static object Vector2(float x, float y)
            => Activator.CreateInstance(T("Godot.Vector2"), x, y);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace DoNotLitterShared;

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

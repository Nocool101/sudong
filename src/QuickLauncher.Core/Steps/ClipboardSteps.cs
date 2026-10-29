using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.Steps;

/// <summary>
/// Win32 剪贴板访问（CF_UNICODETEXT / CF_HDROP）
/// </summary>
public static class ClipboardSteps
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>在 STA 线程执行（剪贴板 API 要求）</summary>
    private static T OnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(3000);
        if (error is not null) throw error;
        return result;
    }

    /// <summary>STA 线程执行（无返回值）</summary>
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(3000);
        if (error is not null) throw error;
    }

    public static string? GetText()
        => OnSta<string?>(() =>
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                var handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return null;
                var ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        });

    public static List<string>? GetFiles()
        => OnSta<List<string>?>(() =>
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                var handle = GetClipboardData(CF_HDROP);
                if (handle == IntPtr.Zero) return null;
                var count = DragQueryFile(handle, 0xFFFFFFFF, null, 0);
                var files = new List<string>();
                var sb = new System.Text.StringBuilder(260);
                for (uint i = 0; i < count; i++)
                {
                    DragQueryFile(handle, i, sb, sb.Capacity);
                    files.Add(sb.ToString());
                }
                return files;
            }
            finally { CloseClipboard(); }
        });

    public static void SetText(string text)
        => OnSta(() =>
        {
            if (!OpenClipboard(IntPtr.Zero)) return;
            try
            {
                EmptyClipboard();
                var bytes = (text.Length + 1) * 2;
                var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                if (hGlobal == IntPtr.Zero) return;
                var target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero) { GlobalFree(hGlobal); return; }
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * 2, 0);
                GlobalUnlock(hGlobal);
                SetClipboardData(CF_UNICODETEXT, hGlobal);
            }
            finally { CloseClipboard(); }
        });

    public static void SetFiles(IEnumerable<string> files)
        => OnSta(() =>
        {
            if (!OpenClipboard(IntPtr.Zero)) return;
            try
            {
                EmptyClipboard();
                // 构造 DROPFILES 结构 + 双 null 结尾的文件名列表
                var list = new List<string>(files);
                var allText = string.Join("\0", list) + "\0\0";
                var textBytes = System.Text.Encoding.Unicode.GetBytes(allText);
                var size = 20 + textBytes.Length; // DROPFILES 头 20 字节
                var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)size);
                if (hGlobal == IntPtr.Zero) return;
                var ptr = GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero) { GlobalFree(hGlobal); return; }
                // DROPFILES: pFiles=20, pt.x=0, pt.y=0, fNC=0, fWide=1
                Marshal.WriteInt32(ptr, 0, 20);
                Marshal.WriteInt32(ptr, 4, 0);
                Marshal.WriteInt32(ptr, 8, 0);
                Marshal.WriteInt32(ptr, 12, 0);
                Marshal.WriteInt32(ptr, 16, 1);
                Marshal.Copy(textBytes, 0, ptr + 20, textBytes.Length);
                GlobalUnlock(hGlobal);
                SetClipboardData(CF_HDROP, hGlobal);
            }
            finally { CloseClipboard(); }
        });

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, System.Text.StringBuilder? lpszFile, int cch);

    #region 步骤定义

    public sealed class GetClipboardText : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "GetClipboardText",
            DisplayName = "获取剪贴板文本",
            Category = "剪贴板",
            Params =
            {
                new StepParamDef { Name = "var", DisplayName = "结果变量名", DefaultValue = "cliptext" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var var = ctx.Interpolate(step.Inputs.GetValueOrDefault("var"));
            if (string.IsNullOrEmpty(var)) var = "cliptext";
            ctx.Set(var, GetText() ?? "");
            return StepResult.Ok();
        }
    }

    public sealed class SetClipboardText : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "SetClipboardText",
            DisplayName = "写入剪贴板文本",
            Category = "剪贴板",
            Params =
            {
                new StepParamDef { Name = "text", DisplayName = "文本", Required = true, Multiline = true }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            SetText(ctx.Interpolate(step.Inputs.GetValueOrDefault("text")) ?? "");
            return StepResult.Ok();
        }
    }

    public sealed class GetSelectedText : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "GetSelectedText",
            DisplayName = "获取选中文本",
            Category = "剪贴板",
            Description = "模拟 Ctrl+C 获取当前选中文本",
            Params =
            {
                new StepParamDef { Name = "var", DisplayName = "结果变量名", DefaultValue = "selection" },
                new StepParamDef { Name = "waitMs", DisplayName = "输入前等待ms", Type = VariableType.Number, DefaultValue = "300" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var var = ctx.Interpolate(step.Inputs.GetValueOrDefault("var"));
            if (string.IsNullOrEmpty(var)) var = "selection";
            if (int.TryParse(ctx.Interpolate(step.Inputs.GetValueOrDefault("waitMs")), out var wait))
                Thread.Sleep(Math.Clamp(wait, 0, 5000));

            KeySyntax.Send("^c");
            Thread.Sleep(120);
            ctx.Set(var, GetText() ?? "");
            return StepResult.Ok();
        }
    }

    public sealed class GetClipboardFiles : IStep
    {
        public StepMetadata Metadata { get; } = new()
        {
            Type = "GetClipboardFiles",
            DisplayName = "获取剪贴板文件列表",
            Category = "剪贴板",
            Params =
            {
                new StepParamDef { Name = "var", DisplayName = "结果变量名", DefaultValue = "clipfiles" }
            }
        };

        public StepResult Execute(StepContext ctx, ActionStep step)
        {
            var var = ctx.Interpolate(step.Inputs.GetValueOrDefault("var"));
            if (string.IsNullOrEmpty(var)) var = "clipfiles";
            var files = GetFiles() ?? new List<string>();
            var list = new List<object?>(files);
            ctx.Set(var, list);
            return StepResult.Ok();
        }
    }

    #endregion
}

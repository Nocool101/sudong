using System;
using System.Collections.Generic;
using QuickLauncher.Engine;
using QuickLauncher.Models;

namespace QuickLauncher.Steps;

/// <summary>
/// 内置步骤汇总注册
/// </summary>
public static class BuiltinSteps
{
    public static IEnumerable<IStep> CreateAll()
    {
        // 常用
        yield return new BasicActionSteps.OpenUrl();
        yield return new BasicActionSteps.OpenFile();
        yield return new BasicActionSteps.OpenFolder();
        yield return new BasicActionSteps.RunProgram();
        yield return new BasicActionSteps.SendText();
        yield return new BasicActionSteps.SendKeys();
        yield return new BasicActionSteps.SwitchPage();
        yield return new FlowSteps.Wait();
        yield return new FlowSteps.Comment();

        // 程序流
        yield return new FlowSteps.If();
        yield return new FlowSteps.Else();
        yield return new FlowSteps.Each();
        yield return new FlowSteps.Repeat();
        yield return new FlowSteps.Break();
        yield return new FlowSteps.Continue();
        yield return new FlowSteps.Stop();
        yield return new FlowSteps.StepGroup();

        // 计算/变量
        yield return new VariableSteps.SetVariable();
        yield return new VariableSteps.ListOperation();

        // 界面交互
        yield return new VariableSteps.ShowMessage();
        yield return new VariableSteps.UserInput();
        yield return new VariableSteps.UserSelect();
        yield return new WindowSteps.ActivateWindow();
        yield return new WindowSteps.WaitWindow();

        // 剪贴板
        yield return new ClipboardSteps.GetClipboardText();
        yield return new ClipboardSteps.SetClipboardText();
        yield return new ClipboardSteps.GetSelectedText();
        yield return new ClipboardSteps.GetClipboardFiles();

        // 系统
        yield return new WindowSteps.GetForegroundWindowInfo();
        yield return new SystemSteps.GetDateTime();
        yield return new SystemSteps.PlaySound();
        yield return new SystemSteps.RunCmd();

        // 文件
        yield return new SystemSteps.ReadTextFile();
        yield return new SystemSteps.WriteTextFile();

        // 网络
        yield return new SystemSteps.WebSearch();

        // 脚本引擎
        yield return new Scripting.ScriptSteps.RunCSharp();
        yield return new Scripting.ScriptSteps.RunJavaScript();
        yield return new Scripting.ScriptSteps.RunPython();
        yield return new Scripting.ScriptSteps.RunScriptFile();

        // 脚本（由 Scripting 层提供，此处不注册；App 启动时补充注册）
    }
}

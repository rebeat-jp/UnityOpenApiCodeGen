#nullable enable



using NUnit.Framework;

using ReBeat.OpenApiCodeGen.Core;

using UnityEngine;

using Assert = NUnit.Framework.Assert;

internal class ProcessTest
{
    [Test]
    public void DockerProcessTest()
    {
        var dockerProcess = new DockerProcess();
        ProcessResponse? showVersionResult = null;
        // 例外が投げられることなく実行完了できるか

        Assert.That(() => showVersionResult = dockerProcess.Send("--version"), Throws.Nothing);
        Debug.Log(showVersionResult?.Message);
        Assert.AreEqual(ExitStatus.Success, showVersionResult?.Status);
        if (showVersionResult?.Status != ExitStatus.Success)
        {
            return;
        }

        // dockerのパスが通る場合

        // run コマンド確認
        var showRunHelpResult = dockerProcess.Send("run --help");
        Debug.Log(showRunHelpResult.Message);
        Assert.AreEqual(ExitStatus.Success, showRunHelpResult.Status);
    }

    [Test]
    public void JavaProcessTest()
    {
        var javaProcess = new JavaProcess();
        ProcessResponse? showVersionResult = null;

        // 例外を投げることなく実行できるか
        Assert.That(() => showVersionResult = javaProcess.Send("--version"),
        Throws.Nothing);
        Debug.Log(showVersionResult?.Message);


    }
}

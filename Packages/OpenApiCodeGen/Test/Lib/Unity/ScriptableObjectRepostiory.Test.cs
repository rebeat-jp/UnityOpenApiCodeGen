#nullable enable

using NUnit.Framework;

using ReBeat.OpenApiCodeGen.Lib;

using UnityEditor;

internal class ScriptableObjectRepositoryTest
{
    [Test]
    public void ReadTest()
    {
        // Setup
        var scriptableObjectRepository = new ScriptableObjectRepository<TestMock>();
        var testMock = new TestMock("Test");
        var testMockAsScriptableObject = testMock.ToScriptable();
        AssetDatabase.CreateAsset(testMockAsScriptableObject, "Assets/Test.asset");
        AssetDatabase.Refresh();

        var readResult = scriptableObjectRepository.Read();
        Assert.NotNull(readResult);
        Assert.AreEqual("Test", readResult?.Title ?? "");

        // clean up
        if (readResult is not null)
        {
            AssetDatabase.DeleteAsset("Assets/Test.asset");
        }
    }

}

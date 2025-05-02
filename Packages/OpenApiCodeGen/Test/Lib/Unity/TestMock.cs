using System;

using NUnit.Framework;

using ReBeat.OpenApiCodeGen.Lib;

using UnityEngine;

class TestMock : IScriptable<TestMock>
{
    public string Title { get; private set; } = "Test";

    public TestMock()
    {
        Title = "";
    }
    public TestMock(string title)
    {
        Title = title;
    }

    public TestMock FromScriptable(ScriptableObject scriptableObject)
    {
        return scriptableObject switch
        {
            TestMockAsScriptableObject testMockAsScriptableObject => new TestMock(testMockAsScriptableObject.Title),
            _ => throw new ArgumentException(),
        };
    }


    public ScriptableObject ToScriptable()
    {
        var scriptableObject = ScriptableObject.CreateInstance<TestMockAsScriptableObject>();
        scriptableObject.Title = Title;
        return scriptableObject;
    }

    public Type GetScriptableType()
    {
        return typeof(TestMockAsScriptableObject);
    }
}
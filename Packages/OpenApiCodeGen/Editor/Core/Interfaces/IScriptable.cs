using System;

using UnityEngine;

namespace ReBeat.OpenApiCodeGen.Lib
{
    public interface IScriptable<T> where T : class
    {
        ScriptableObject ToScriptable();

        T FromScriptable(ScriptableObject scriptableObject);
        Type GetScriptableType();
    }
}
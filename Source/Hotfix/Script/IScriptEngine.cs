using System.Collections.Generic;
using System.IO;

namespace Hotfix;

public interface IScriptEngine
{
    IScriptState CreateState();

    byte[] Compile(string assemblyName, IReadOnlyDictionary<string, string> sources);

    byte[] Compile(string assemblyName, IReadOnlyDictionary<string, string> sources, BuildOptions build);
}

public interface IScriptState
{
    IScriptRun Prepare(string code);
}

public interface IScriptRun
{
    TextWriter Output { get; }

    string Execute(ScriptLimit limit);
}

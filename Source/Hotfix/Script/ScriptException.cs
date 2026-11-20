using System;

namespace Hotfix;

public sealed class ScriptException(string message) : Exception(message);

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Hotfix;

internal enum SubModStatus { On, Off, Restart, Failed, Missing, Incompatible }

// SubMod.xml
internal sealed class SubMod
{
    // 폴더 이름
    [XmlIgnore] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime Created { get; set; }
    // 만든 대화의 id
    public string? Chat { get; set; }
    public int Revision { get; set; }
    public List<string> SupportedVersions { get; set; } = [];
    public List<string> Requires { get; set; } = [];
    // 오류 때문에 꺼졌으면 그 이유
    public string? DisabledReason { get; set; }
}

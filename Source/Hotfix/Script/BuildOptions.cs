using System.Collections.Generic;

namespace Hotfix;

// IScriptEngine.Compile의 선택 사항. 기본값은 서브 모드를 빌드할 때와 같다
public sealed record BuildOptions
{
    // 최적화하지 않고 DEBUG를 정의하고 디버그 정보를 DLL 안에 넣는다
    public bool Debug { get; init; }
    public IReadOnlyList<string> Defines { get; init; } = [];
    // null이면 최신
    public string? LanguageVersion { get; init; }
    // 로드된 어셈블리 말고 더 참조할 DLL 파일
    public IReadOnlyList<string> References { get; init; } = [];
    // 로드된 어셈블리 가운데 참조하지 않을 것의 이름
    public IReadOnlyList<string> Exclude { get; init; } = [];
}

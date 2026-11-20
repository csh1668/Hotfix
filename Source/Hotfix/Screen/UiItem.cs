using UnityEngine;

namespace Hotfix;

public sealed record UiItem(string Role, Rect Box, string Text, bool Checked = false, int[]? Groups = null);

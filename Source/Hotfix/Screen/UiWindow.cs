using System.Collections.Generic;
using UnityEngine;

namespace Hotfix;

public sealed record UiWindow(string Type, string Title, Rect Box, List<UiItem> Items);

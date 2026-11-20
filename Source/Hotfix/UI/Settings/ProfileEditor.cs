using System;
using System.Linq;
using Hotfix.Data;
using Rimact;
using Rimact.Components;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 모달 창의 내용. 초안을 고치다가 확인을 누르면 프로필에 적용한다
internal sealed record ProfileEditor(HotfixSettings Settings, LlmProfile Profile, Action OnClose) : UIComponent
{
    private const float LabelWidth = 110f;
    private const float PickerWidth = 220f;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var backend = UseHost().Backend;
        var settings = Settings;
        var profile = Profile;
        var close = OnClose;
        var draft = UseMemo(() =>
        {
            var copy = new LlmProfile();
            copy.CopyFrom(profile);
            return copy;
        });
        var fetching = UseState(false);
        var fetchError = UseState("");
        UseExternal(() => (draft.Name, draft.ProviderId, draft.ApiKey, draft.BaseUrl, draft.Model, draft.Effort, draft.ContextWindow,
            draft.InputPrice, draft.OutputPrice, draft.CacheReadPrice, draft.CacheWritePrice));
        var provider = draft.Provider;
        // CLI는 로그인된 계정을 쓰므로 키가 없고, 주소 자리에 실행 파일을 적는다
        var cli = provider is Provider_Cli;

        (LlmEffort?, string)[] efforts =
        [
            (null, "Hotfix_EffortDefault".Translate()),
            .. Enum.GetValues<LlmEffort>().Select(effort => ((LlmEffort?)effort, effort.ToString())),
        ];
        var fetch = Button("Hotfix_FetchModels".Translate(), FetchModels) with { Loading = fetching.Value };
        if (provider is null)
            fetch = fetch.Disabled();

        return Column(
            Field("Hotfix_Name", Input(draft.Name, value => draft.Name = value)),
            Field("Hotfix_Provider", Select(draft.ProviderId, Providers.List.Select(p => (p.Id, p.Label)), id =>
            {
                if (draft.ProviderId == id)
                    return;
                draft.ProviderId = id;
                draft.BaseUrl = null;
                draft.Model = "";
                fetchError.Set("");
            }, placeholder: draft.ProviderId).Width(PickerWidth)),
            cli ? null : Field("Hotfix_ApiKey", Input(draft.ApiKey, value => draft.ApiKey = value.Trim())),
            Field(cli ? "Hotfix_Command" : "Hotfix_BaseUrl", Input(draft.BaseUrl ?? "",
                value => draft.BaseUrl = value.Trim() is { Length: > 0 } url ? url : null,
                placeholder: provider?.DefaultBaseUrl)),
            Field("Hotfix_Model", Row(
                Input(draft.Model, value => draft.Model = value.Trim()).Grow(),
                fetch
            ).Gap(t.Space(2)), fetchError.Value),
            Field("Hotfix_Effort",
                Select(draft.Effort, efforts, effort => draft.Effort = effort).Width(PickerWidth)),
            Field("Hotfix_ContextWindow",
                IntField(draft.ContextWindow, value => draft.ContextWindow = value, min: 0, step: 10_000)),
            Separator("Hotfix_Prices".Translate()),
            Field("Hotfix_PriceInput", Price(draft.InputPrice, value => draft.InputPrice = value)),
            Field("Hotfix_PriceOutput", Price(draft.OutputPrice, value => draft.OutputPrice = value)),
            Field("Hotfix_PriceCacheRead", Price(draft.CacheReadPrice, value => draft.CacheReadPrice = value)),
            Field("Hotfix_PriceCacheWrite", Price(draft.CacheWritePrice, value => draft.CacheWritePrice = value)),
            DialogFooter(
                Button("CancelButton".Translate(), close) with { IsCancel = true },
                Button("Confirm".Translate(), () =>
                {
                    profile.CopyFrom(draft);
                    if (!settings.Profiles.Contains(profile))
                        settings.Profiles.Add(profile);
                    close();
                }, ButtonVariant.Primary) with
                { IsDefault = true })
        ).Gap(t.Space(2));

        static Node Field(string label, Node control, string? error = null) =>
            FieldRow(label.Translate(), control, error: error) with { LabelWidth = LabelWidth };

        static Node Price(float value, Action<float> set) => NumberField(value, set, min: 0f, step: 0.5f, decimals: 4);

        async void FetchModels()
        {
            fetching.Set(true);
            fetchError.Set("");
            try
            {
                var (models, error) = await provider!.GetModelsAsync(draft.Endpoint);
                if (error is not null)
                    fetchError.Set(error.Message);
                else if (models.Count > 0)
                    backend.OpenMenu(models.Select(model => MenuItem(model, () => draft.Model = model)).ToList());
            }
            catch (Exception e)
            {
                fetchError.Set(e.Message);
            }
            finally
            {
                fetching.Set(false);
            }
        }
    }
}

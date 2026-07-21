#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.localization;
using nadena.dev.ndmf.ui;
using UnityEditor;

namespace THE_cattail.AfkMotionPatcher
{
    [InitializeOnLoad]
    internal static class AfkMotionPatchLocalization
    {
        internal static readonly string[] SupportedLanguages =
        {
            "en-US", "ja-JP", "zh-Hans", "zh-Hant", "ko-KR"
        };

        private static readonly Dictionary<string, string> Japanese = new Dictionary<string, string>
        {
            ["AFK Motion Preview"] = "AFKモーションプレビュー",
            ["Source"] = "ソース",
            ["Result"] = "結果",
            ["Play"] = "再生",
            ["Pause"] = "一時停止",
            ["Stop"] = "停止",
            ["Loop"] = "ループ",
            ["Reset View"] = "表示をリセット",
            ["Preview root not resolved"] = "プレビュー対象を解決できません",
            ["Open Preview"] = "プレビューを開く",
            ["AFK Source"] = "AFKソース",
            ["Avatar"] = "アバター",
            ["Hierarchy Mappings ({0})"] = "階層マッピング ({0})",
            ["Maps animated objects or bones between different source and target Avatar hierarchies."] = "ソースとターゲットのAvatar間で、アニメーション対象やボーンの階層を対応付けます。",
            ["BlendShape Mappings ({0})"] = "ブレンドシェイプマッピング ({0})",
            ["Maps a source BlendShape to the corresponding BlendShape on the target avatar."] = "ソースのブレンドシェイプをターゲットアバター上の対応するブレンドシェイプへ割り当てます。",
            ["Curve Adjustments ({0})"] = "カーブ調整 ({0})",
            ["Adjusts animation curve values before they are copied or mapped."] = "コピーまたはマッピングの前にアニメーションカーブの値を調整します。",
            ["Advanced Binding Mappings ({0})"] = "高度なバインディングマッピング ({0})",
            ["Maps one animation property to an additional target property with optional value adjustment."] = "1つのアニメーションプロパティを追加のターゲットプロパティへ割り当て、必要に応じて値を調整します。",
            ["Avatar root not found. Place this component anywhere under a VRChat avatar."] = "アバターのルートが見つかりません。このコンポーネントをVRChatアバター配下に配置してください。",
            ["Multiple active AFK Motion Patch components were found under this Avatar. Keep exactly one enabled component."] = "このアバター配下に有効なAFK Motion Patchが複数あります。有効なコンポーネントを1つだけにしてください。",
            ["Source Avatar is missing. Assign the Avatar prefab whose AFK motion is being adapted."] = "ソースアバターが未設定です。適用元AFKモーションを持つアバタープレハブを指定してください。",
            ["The adapter preset's internal source Motion data is invalid. Update or reimport the preset. {0}"] = "アダプタープリセット内部のソースMotionデータが無効です。プリセットを更新または再インポートしてください。{0}",
            ["The AFK source graph and adapter preset provide no usable Motion. Update or reimport the source package and preset."] = "AFKソースグラフとアダプタープリセットに使用可能なMotionがありません。ソースパッケージとプリセットを更新または再インポートしてください。",
            ["AFK source graph error: {0}"] = "AFKソースグラフエラー: {0}",
            ["Target"] = "ターゲット",
            ["<Avatar Root>"] = "<アバタールート>",
            ["Select Source Path"] = "ソースパスを選択",
            ["Select Target Object"] = "ターゲットオブジェクトを選択",
            ["Choose an object under the same avatar."] = "同じアバター配下のオブジェクトを選択してください。",
            ["OK"] = "OK",
            ["Select..."] = "選択...",
            ["Select Source BlendShape"] = "ソースのブレンドシェイプを選択",
            ["Select Target BlendShape"] = "ターゲットのブレンドシェイプを選択",
            ["(None)"] = "(なし)",
            ["Source Binding"] = "ソースバインディング",
            ["Target Binding"] = "ターゲットバインディング",
            ["Select Source Binding"] = "ソースバインディングを選択",
            ["Select Target Binding"] = "ターゲットバインディングを選択",
            ["<All Source Bindings>"] = "<すべてのソースバインディング>",
            ["Use Source Binding"] = "ソースバインディングを使用",
            ["Binding"] = "バインディング",
            ["Scale"] = "スケール",
            ["Offset"] = "オフセット",
            ["Clamp"] = "制限",
            ["Minimum"] = "最小",
            ["Maximum"] = "最大",
            ["Copy Property Path"] = "プロパティパスをコピー",
            ["Apply to Prefab '{0}'"] = "プレハブ '{0}' に適用",
            ["Apply to Prefab"] = "プレハブに適用",
            ["Revert Group"] = "グループを元に戻す",
            ["Revert Item"] = "項目を元に戻す",
            ["Copy Group"] = "グループをコピー",
            ["Paste Group"] = "グループを貼り付け",
            ["Any Component"] = "すべてのコンポーネント",
            ["Transform"] = "Transform",
            ["Skinned Mesh Renderer"] = "Skinned Mesh Renderer"
        };

        private static readonly Dictionary<string, string> SimplifiedChinese = new Dictionary<string, string>
        {
            ["AFK Motion Preview"] = "AFK 动作预览",
            ["Source"] = "来源",
            ["Result"] = "结果",
            ["Play"] = "播放",
            ["Pause"] = "暂停",
            ["Stop"] = "停止",
            ["Loop"] = "循环",
            ["Reset View"] = "重置视图",
            ["Preview root not resolved"] = "无法解析预览对象",
            ["Open Preview"] = "打开预览",
            ["AFK Source"] = "AFK 来源",
            ["Avatar"] = "Avatar",
            ["Hierarchy Mappings ({0})"] = "层级映射（{0}）",
            ["Maps animated objects or bones between different source and target Avatar hierarchies."] = "映射来源与目标 Avatar 中层级不同的动画对象或骨骼。",
            ["BlendShape Mappings ({0})"] = "形态键映射（{0}）",
            ["Maps a source BlendShape to the corresponding BlendShape on the target avatar."] = "将来源形态键映射到目标 Avatar 上对应的形态键。",
            ["Curve Adjustments ({0})"] = "曲线调整（{0}）",
            ["Adjusts animation curve values before they are copied or mapped."] = "在复制或映射前调整动画曲线值。",
            ["Advanced Binding Mappings ({0})"] = "高级绑定映射（{0}）",
            ["Maps one animation property to an additional target property with optional value adjustment."] = "将一个动画属性额外映射到另一个目标属性，并可调整数值。",
            ["Avatar root not found. Place this component anywhere under a VRChat avatar."] = "未找到 Avatar 根节点。请将此组件放在 VRChat Avatar 下的任意位置。",
            ["Multiple active AFK Motion Patch components were found under this Avatar. Keep exactly one enabled component."] = "此 Avatar 下存在多个启用的 AFK Motion Patch。请只保留一个启用的组件。",
            ["Source Avatar is missing. Assign the Avatar prefab whose AFK motion is being adapted."] = "缺少来源 Avatar。请指定提供 AFK 动作的 Avatar prefab。",
            ["The adapter preset's internal source Motion data is invalid. Update or reimport the preset. {0}"] = "适配器预设内部的来源 Motion 数据无效。请更新或重新导入预设。{0}",
            ["The AFK source graph and adapter preset provide no usable Motion. Update or reimport the source package and preset."] = "AFK 来源图和适配器预设没有可用 Motion。请更新或重新导入来源资源包和预设。",
            ["AFK source graph error: {0}"] = "AFK 来源图错误：{0}",
            ["Target"] = "目标",
            ["<Avatar Root>"] = "<Avatar 根节点>",
            ["Select Source Path"] = "选择来源路径",
            ["Select Target Object"] = "选择目标对象",
            ["Choose an object under the same avatar."] = "请选择同一 Avatar 下的对象。",
            ["OK"] = "确定",
            ["Select..."] = "选择...",
            ["Select Source BlendShape"] = "选择来源形态键",
            ["Select Target BlendShape"] = "选择目标形态键",
            ["(None)"] = "（无）",
            ["Source Binding"] = "来源绑定",
            ["Target Binding"] = "目标绑定",
            ["Select Source Binding"] = "选择来源绑定",
            ["Select Target Binding"] = "选择目标绑定",
            ["<All Source Bindings>"] = "<全部来源绑定>",
            ["Use Source Binding"] = "使用来源绑定",
            ["Binding"] = "绑定",
            ["Scale"] = "缩放",
            ["Offset"] = "偏移",
            ["Clamp"] = "限制",
            ["Minimum"] = "最小值",
            ["Maximum"] = "最大值",
            ["Copy Property Path"] = "复制属性路径",
            ["Apply to Prefab '{0}'"] = "应用到 Prefab“{0}”",
            ["Apply to Prefab"] = "应用到 Prefab",
            ["Revert Group"] = "恢复分组",
            ["Revert Item"] = "恢复此项",
            ["Copy Group"] = "复制分组",
            ["Paste Group"] = "粘贴分组",
            ["Any Component"] = "任意组件",
            ["Transform"] = "Transform",
            ["Skinned Mesh Renderer"] = "Skinned Mesh Renderer"
        };

        private static readonly Dictionary<string, string> TraditionalChinese = new Dictionary<string, string>
        {
            ["AFK Motion Preview"] = "AFK 動作預覽",
            ["Source"] = "來源",
            ["Result"] = "結果",
            ["Play"] = "播放",
            ["Pause"] = "暫停",
            ["Stop"] = "停止",
            ["Loop"] = "循環",
            ["Reset View"] = "重設視圖",
            ["Preview root not resolved"] = "無法解析預覽物件",
            ["Open Preview"] = "開啟預覽",
            ["AFK Source"] = "AFK 來源",
            ["Avatar"] = "Avatar",
            ["Hierarchy Mappings ({0})"] = "階層映射（{0}）",
            ["Maps animated objects or bones between different source and target Avatar hierarchies."] = "映射來源與目標 Avatar 中階層不同的動畫物件或骨骼。",
            ["BlendShape Mappings ({0})"] = "形態鍵映射（{0}）",
            ["Maps a source BlendShape to the corresponding BlendShape on the target avatar."] = "將來源形態鍵映射至目標 Avatar 上對應的形態鍵。",
            ["Curve Adjustments ({0})"] = "曲線調整（{0}）",
            ["Adjusts animation curve values before they are copied or mapped."] = "在複製或映射前調整動畫曲線值。",
            ["Advanced Binding Mappings ({0})"] = "進階綁定映射（{0}）",
            ["Maps one animation property to an additional target property with optional value adjustment."] = "將一個動畫屬性額外映射至另一個目標屬性，並可調整數值。",
            ["Avatar root not found. Place this component anywhere under a VRChat avatar."] = "找不到 Avatar 根節點。請將此元件放在 VRChat Avatar 下的任意位置。",
            ["Multiple active AFK Motion Patch components were found under this Avatar. Keep exactly one enabled component."] = "此 Avatar 下有多個啟用的 AFK Motion Patch。請只保留一個啟用的元件。",
            ["Source Avatar is missing. Assign the Avatar prefab whose AFK motion is being adapted."] = "缺少來源 Avatar。請指定提供 AFK 動作的 Avatar prefab。",
            ["The adapter preset's internal source Motion data is invalid. Update or reimport the preset. {0}"] = "適配器預設內部的來源 Motion 資料無效。請更新或重新匯入預設。{0}",
            ["The AFK source graph and adapter preset provide no usable Motion. Update or reimport the source package and preset."] = "AFK 來源圖與適配器預設沒有可用 Motion。請更新或重新匯入來源資源包與預設。",
            ["AFK source graph error: {0}"] = "AFK 來源圖錯誤：{0}",
            ["Target"] = "目標",
            ["<Avatar Root>"] = "<Avatar 根節點>",
            ["Select Source Path"] = "選擇來源路徑",
            ["Select Target Object"] = "選擇目標物件",
            ["Choose an object under the same avatar."] = "請選擇同一 Avatar 下的物件。",
            ["OK"] = "確定",
            ["Select..."] = "選擇...",
            ["Select Source BlendShape"] = "選擇來源形態鍵",
            ["Select Target BlendShape"] = "選擇目標形態鍵",
            ["(None)"] = "（無）",
            ["Source Binding"] = "來源綁定",
            ["Target Binding"] = "目標綁定",
            ["Select Source Binding"] = "選擇來源綁定",
            ["Select Target Binding"] = "選擇目標綁定",
            ["<All Source Bindings>"] = "<全部來源綁定>",
            ["Use Source Binding"] = "使用來源綁定",
            ["Binding"] = "綁定",
            ["Scale"] = "縮放",
            ["Offset"] = "偏移",
            ["Clamp"] = "限制",
            ["Minimum"] = "最小值",
            ["Maximum"] = "最大值",
            ["Copy Property Path"] = "複製屬性路徑",
            ["Apply to Prefab '{0}'"] = "套用至 Prefab「{0}」",
            ["Apply to Prefab"] = "套用至 Prefab",
            ["Revert Group"] = "還原分組",
            ["Revert Item"] = "還原此項",
            ["Copy Group"] = "複製分組",
            ["Paste Group"] = "貼上分組",
            ["Any Component"] = "任意元件",
            ["Transform"] = "Transform",
            ["Skinned Mesh Renderer"] = "Skinned Mesh Renderer"
        };

        private static readonly Dictionary<string, string> Korean = new Dictionary<string, string>
        {
            ["AFK Motion Preview"] = "AFK 모션 미리보기",
            ["Source"] = "소스",
            ["Result"] = "결과",
            ["Play"] = "재생",
            ["Pause"] = "일시 정지",
            ["Stop"] = "정지",
            ["Loop"] = "반복",
            ["Reset View"] = "보기 초기화",
            ["Preview root not resolved"] = "미리보기 대상을 찾을 수 없습니다",
            ["Open Preview"] = "미리보기 열기",
            ["AFK Source"] = "AFK 소스",
            ["Avatar"] = "아바타",
            ["Hierarchy Mappings ({0})"] = "계층 매핑 ({0})",
            ["Maps animated objects or bones between different source and target Avatar hierarchies."] = "소스와 대상 아바타 사이에서 애니메이션 오브젝트나 본의 계층을 매핑합니다.",
            ["BlendShape Mappings ({0})"] = "블렌드셰이프 매핑 ({0})",
            ["Maps a source BlendShape to the corresponding BlendShape on the target avatar."] = "소스 블렌드셰이프를 대상 아바타의 해당 블렌드셰이프에 매핑합니다.",
            ["Curve Adjustments ({0})"] = "커브 조정 ({0})",
            ["Adjusts animation curve values before they are copied or mapped."] = "복사 또는 매핑 전에 애니메이션 커브 값을 조정합니다.",
            ["Advanced Binding Mappings ({0})"] = "고급 바인딩 매핑 ({0})",
            ["Maps one animation property to an additional target property with optional value adjustment."] = "하나의 애니메이션 프로퍼티를 추가 대상 프로퍼티에 매핑하고 필요에 따라 값을 조정합니다.",
            ["Avatar root not found. Place this component anywhere under a VRChat avatar."] = "아바타 루트를 찾을 수 없습니다. 이 컴포넌트를 VRChat 아바타 아래에 배치하세요.",
            ["Multiple active AFK Motion Patch components were found under this Avatar. Keep exactly one enabled component."] = "이 아바타 아래에 활성 AFK Motion Patch가 여러 개 있습니다. 활성 컴포넌트를 하나만 남겨 주세요.",
            ["Source Avatar is missing. Assign the Avatar prefab whose AFK motion is being adapted."] = "소스 아바타가 없습니다. AFK 모션을 제공하는 아바타 프리팹을 지정하세요.",
            ["The adapter preset's internal source Motion data is invalid. Update or reimport the preset. {0}"] = "어댑터 프리셋 내부의 소스 Motion 데이터가 올바르지 않습니다. 프리셋을 업데이트하거나 다시 가져오세요. {0}",
            ["The AFK source graph and adapter preset provide no usable Motion. Update or reimport the source package and preset."] = "AFK 소스 그래프와 어댑터 프리셋에 사용 가능한 Motion이 없습니다. 소스 패키지와 프리셋을 업데이트하거나 다시 가져오세요.",
            ["AFK source graph error: {0}"] = "AFK 소스 그래프 오류: {0}",
            ["Target"] = "대상",
            ["<Avatar Root>"] = "<아바타 루트>",
            ["Select Source Path"] = "소스 경로 선택",
            ["Select Target Object"] = "대상 오브젝트 선택",
            ["Choose an object under the same avatar."] = "같은 아바타 아래의 오브젝트를 선택하세요.",
            ["OK"] = "확인",
            ["Select..."] = "선택...",
            ["Select Source BlendShape"] = "소스 블렌드셰이프 선택",
            ["Select Target BlendShape"] = "대상 블렌드셰이프 선택",
            ["(None)"] = "(없음)",
            ["Source Binding"] = "소스 바인딩",
            ["Target Binding"] = "대상 바인딩",
            ["Select Source Binding"] = "소스 바인딩 선택",
            ["Select Target Binding"] = "대상 바인딩 선택",
            ["<All Source Bindings>"] = "<모든 소스 바인딩>",
            ["Use Source Binding"] = "소스 바인딩 사용",
            ["Binding"] = "바인딩",
            ["Scale"] = "스케일",
            ["Offset"] = "오프셋",
            ["Clamp"] = "제한",
            ["Minimum"] = "최솟값",
            ["Maximum"] = "최댓값",
            ["Copy Property Path"] = "프로퍼티 경로 복사",
            ["Apply to Prefab '{0}'"] = "프리팹 '{0}'에 적용",
            ["Apply to Prefab"] = "프리팹에 적용",
            ["Revert Group"] = "그룹 되돌리기",
            ["Revert Item"] = "항목 되돌리기",
            ["Copy Group"] = "그룹 복사",
            ["Paste Group"] = "그룹 붙여넣기",
            ["Any Component"] = "모든 컴포넌트",
            ["Transform"] = "Transform",
            ["Skinned Mesh Renderer"] = "Skinned Mesh Renderer"
        };

        private static readonly Localizer Localizer = new Localizer("en-US", () =>
        {
            var englishKeys = Japanese.Keys
                .Concat(SimplifiedChinese.Keys)
                .Concat(TraditionalChinese.Keys)
                .Concat(Korean.Keys)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(key => key, key => key, StringComparer.Ordinal);
            return new List<(string, Func<string, string>)>
            {
                ("en-US", Lookup(englishKeys)),
                ("ja-JP", Lookup(Japanese)),
                ("zh-Hans", Lookup(SimplifiedChinese)),
                ("zh-Hant", Lookup(TraditionalChinese)),
                ("ko-KR", Lookup(Korean))
            };
        });

        static AfkMotionPatchLocalization()
        {
            foreach (var language in SupportedLanguages) LanguagePrefs.RegisterLanguage(language);
        }

        internal static string T(string english)
        {
            return Localizer.TryGetLocalizedString(english, out var localized) ? localized : english;
        }

        internal static string F(string english, params object[] args)
        {
            try
            {
                return string.Format(T(english), args);
            }
            catch (FormatException)
            {
                return english + " (" + string.Join(", ", args.Select(arg => arg?.ToString() ?? "null")) + ")";
            }
        }

        internal static void DrawLanguageSwitcher()
        {
            LanguageSwitcher.DrawImmediate();
        }

        private static Func<string, string> Lookup(IReadOnlyDictionary<string, string> dictionary)
        {
            return key => dictionary.TryGetValue(key, out var value) ? value : null;
        }
    }
}
#endif

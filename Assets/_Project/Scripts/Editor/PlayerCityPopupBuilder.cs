using NomadCity.App;
using NomadCity.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NomadCity.EditorTools
{
    /// <summary>
    /// 一键在 Game 场景里生成「玩家城市面板弹窗」的骨架并接好全部引用：
    /// 宿主 → Root → Dim / Panel（CityPanel.png 底板）/ 标题 / 12 个格子 / 关闭按钮。
    ///
    /// 这个弹窗由玩家状态栏每行左侧那颗眼睛按钮打开（<see cref="GameHud.SeatInspectRequested"/> →
    /// <see cref="PlayerCityPopup.Show"/>），内容是那名玩家的 12 格城市面板，只读。
    ///
    /// 格子坐标是从场景里常驻的 CityPanelView 量下来的（底板 411×760、格子 120×170、
    /// 列心 x = ±126、行心 y = 264 / 88 / -88 / -264），所以和 CityPanel.png 的框线严丝合缝；
    /// 想整体放大就改 Panel 的宽高（保持 411:760），格子会按比例跟着缩放到对应位置。
    ///
    /// 用法：打开 Game 场景 → 菜单 NomadCity / 界面：生成玩家城市弹窗（Player City Popup）。
    /// </summary>
    public static class PlayerCityPopupBuilder
    {
        private const string HostName = "PlayerCityPopup";
        private const string CityPanelSpritePath = "Assets/_Project/Art/CityPanel.png";

        /// <summary>底板设计尺寸（= CityPanel.png 的宽高比，格子坐标都按这个量出来的）。</summary>
        private const float BasePanelWidth = 411f;
        private const float BasePanelHeight = 760f;

        private const float BaseSlotWidth = 120f;
        private const float BaseSlotHeight = 170f;

        [MenuItem("NomadCity/界面：生成玩家城市弹窗（Player City Popup）")]
        private static void Build()
        {
            PlayerCityPopup existing = Object.FindAnyObjectByType<PlayerCityPopup>(FindObjectsInactive.Include);
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("玩家城市弹窗已存在",
                    "场景里已经有 PlayerCityPopup。\n\n" +
                    "重建：删掉它，按标准尺寸重新生成（布局最干净）。\n" +
                    "复用接线：保留现有布局，只重新接一遍引用与卡面库。",
                    "重建", "复用接线");

                if (rebuild) Undo.DestroyObjectImmediate(existing.gameObject);
                else
                {
                    Rewire(existing);
                    return;
                }
            }

            Canvas canvas = FindTargetCanvas();
            if (canvas == null)
            {
                Debug.LogError("[玩家城市] 场景里找不到 Canvas，无法生成弹窗。请先打开 Game 场景。");
                return;
            }

            Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CityPanelSpritePath);
            if (panelSprite == null)
                Debug.LogWarning($"[玩家城市] 读不到底板图 {CityPanelSpritePath}，底板会用一块纯色代替，格子位置照旧。");
            Sprite buttonSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            // —— 宿主 ——
            var host = new GameObject(HostName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(host, "生成玩家城市弹窗");
            GameObjectUtility.SetParentAndAlign(host, canvas.gameObject);
            Stretch((RectTransform)host.transform, 0f, 0f, 0f, 0f);
            var popup = host.AddComponent<PlayerCityPopup>();

            // —— Root（弹窗开关）——
            RectTransform root = NewRect("Root", host.transform);
            Stretch(root, 0f, 0f, 0f, 0f);

            // —— Dim（全屏遮罩；点一下就收起）——
            RectTransform dim = NewRect("Dim", root);
            Stretch(dim, 0f, 0f, 0f, 0f);
            Image dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.65f);
            dimImage.raycastTarget = true;
            Button dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.targetGraphic = dimImage;

            // —— Panel（城市面板底板）——
            RectTransform panel = NewRect("Panel", root);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(BasePanelWidth, BasePanelHeight);
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = panelSprite;
            panelImage.type = Image.Type.Simple;
            panelImage.preserveAspect = false;   // 尺寸本身就是 411:760，不用再等比
            panelImage.color = panelSprite != null ? Color.white : new Color(0.18f, 0.17f, 0.16f, 1f);
            panelImage.raycastTarget = true;

            // —— 标题（浮在底板正上方，不挤占格子）——
            RectTransform titleRect = NewRect("Title", root);
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0f);        // 底边贴住底板的上沿
            titleRect.anchoredPosition = new Vector2(0f, BasePanelHeight * 0.5f + 8f);
            titleRect.sizeDelta = new Vector2(BasePanelWidth, 40f);
            TextMeshProUGUI titleText = titleRect.gameObject.AddComponent<TextMeshProUGUI>();
            titleText.text = "某某 的城市";
            titleText.fontSize = 30f;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;
            titleText.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) titleText.font = TMP_Settings.defaultFontAsset;

            // —— CityView（挂 CityPanelView 的内容层，12 个格子）——
            RectTransform cityViewRect = NewRect("CityView", panel);
            Stretch(cityViewRect, 0f, 0f, 0f, 0f);
            var cityView = cityViewRect.gameObject.AddComponent<CityPanelView>();

            Button[] slotButtons = new Button[12];
            Image[] slotImages = new Image[12];
            Sprite slotSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            int index = 0;
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    RectTransform slot = NewRect($"Slot {index + 1}", cityViewRect);
                    slot.anchorMin = new Vector2(0.5f, 0.5f);
                    slot.anchorMax = new Vector2(0.5f, 0.5f);
                    slot.pivot = new Vector2(0.5f, 0.5f);
                    slot.sizeDelta = new Vector2(BaseSlotWidth, BaseSlotHeight);
                    slot.anchoredPosition = new Vector2((col - 1) * 126f, 264f - row * 176f);

                    Image slotImage = slot.gameObject.AddComponent<Image>();
                    slotImage.sprite = slotSprite;
                    slotImage.type = Image.Type.Sliced;
                    slotImage.color = new Color(1f, 1f, 1f, 0f);   // 空格子透明，露出底板
                    slotImage.raycastTarget = true;

                    Button slotButton = slot.gameObject.AddComponent<Button>();
                    slotButton.targetGraphic = slotImage;

                    slotButtons[index] = slotButton;
                    slotImages[index] = slotImage;
                    index++;
                }
            }

            // —— 关闭按钮（右上角，浮在底板外侧一点，不压格子）——
            RectTransform close = NewRect("CloseButton", root);
            close.anchorMin = new Vector2(0.5f, 0.5f);
            close.anchorMax = new Vector2(0.5f, 0.5f);
            close.pivot = new Vector2(0f, 0f);
            close.anchoredPosition = new Vector2(BasePanelWidth * 0.5f + 8f, BasePanelHeight * 0.5f - 60f);
            close.sizeDelta = new Vector2(52f, 52f);
            Image closeImage = close.gameObject.AddComponent<Image>();
            closeImage.sprite = buttonSprite;
            closeImage.type = Image.Type.Sliced;
            closeImage.color = new Color(0.85f, 0.86f, 0.88f, 1f);
            closeImage.raycastTarget = true;
            Button closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = closeImage;

            RectTransform closeLabel = NewRect("Text", close);
            Stretch(closeLabel, 0f, 0f, 0f, 0f);
            TextMeshProUGUI closeText = closeLabel.gameObject.AddComponent<TextMeshProUGUI>();
            closeText.text = "×";
            closeText.fontSize = 34f;
            closeText.alignment = TextAlignmentOptions.Center;
            closeText.color = new Color(0.12f, 0.12f, 0.14f, 1f);
            closeText.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) closeText.font = TMP_Settings.defaultFontAsset;

            // —— 接线 ——
            Sprite[] library = Wire(popup, root, titleText, closeButton, dimButton, cityView, slotButtons, slotImages);
            root.gameObject.SetActive(false);

            // 叠放：紧贴在「卡面放大弹窗」下面一层 —— 放大弹窗永远最上，其余情况它盖住常驻 UI。
            Transform preview = FindChildByName(canvas.transform, "CardPreviewPopup");
            if (preview != null) host.transform.SetSiblingIndex(preview.GetSiblingIndex());
            else host.transform.SetAsLastSibling();

            int wired = WireGameBootstrap(popup);

            EditorSceneManager.MarkSceneDirty(host.scene);
            Selection.activeGameObject = host;
            Debug.Log($"[玩家城市] 已生成弹窗骨架并接好引用（卡面库 {library.Length} 项）。" +
                      (wired > 0
                          ? $"并把 GameBootstrap 的「Player City Popup」接上了（{wired} 处）。"
                          : "场景里没找到 GameBootstrap，请手动把 PlayerCityPopup 拖到它的「Player City Popup」字段。") +
                      "\n状态栏左侧的眼睛按钮点开的就是这块面板；想整体放大就改 Panel 的宽高（保持 411:760）。");
        }

        /// <summary>场景里已有弹窗时：按名字找回子物体，重新接一遍引用与卡面库（保留现有布局）。</summary>
        private static void Rewire(PlayerCityPopup popup)
        {
            Transform root = FindDeep(popup.transform, "Root");
            Transform title = FindDeep(popup.transform, "Title");
            Transform close = FindDeep(popup.transform, "CloseButton");
            Transform dim = FindDeep(popup.transform, "Dim");
            Transform cityViewTransform = FindDeep(popup.transform, "CityView");

            if (root == null || close == null || cityViewTransform == null)
            {
                Debug.LogError("[玩家城市] 现有弹窗缺 Root / CloseButton / CityView，没法自动接线。" +
                               "请再执行一次本菜单并选「重建」。");
                return;
            }

            Button[] slotButtons;
            Image[] slotImages;
            CollectSlots(cityViewTransform, out slotButtons, out slotImages);

            CityPanelView cityView = cityViewTransform.GetComponent<CityPanelView>();
            if (cityView == null) cityView = Undo.AddComponent<CityPanelView>(cityViewTransform.gameObject);

            Sprite[] library = Wire(popup, root,
                title != null ? title.GetComponent<TMP_Text>() : null,
                close.GetComponent<Button>(),
                dim != null ? dim.GetComponent<Button>() : null,
                cityView, slotButtons, slotImages);

            int wired = WireGameBootstrap(popup);
            EditorSceneManager.MarkSceneDirty(popup.gameObject.scene);
            Selection.activeGameObject = popup.gameObject;
            Debug.Log($"[玩家城市] 已复用现有弹窗并重新接线（卡面库 {library.Length} 项，" +
                      $"格子 {slotButtons.Length} 个）。" +
                      (wired > 0 ? $"GameBootstrap 接线 {wired} 处。" : "GameBootstrap 没接上，需要手动拖。"));
        }

        /// <summary>把弹窗上的引用、12 个格子与卡面库写进去，返回填好的卡面库。</summary>
        private static Sprite[] Wire(PlayerCityPopup popup, Transform root, TMP_Text title, Button closeButton,
            Button dimButton, CityPanelView cityView, Button[] slotButtons, Image[] slotImages)
        {
            Sprite[] library = CardFaceFiller.BuildFacilityLibrary();
            if (library == null)
            {
                Debug.LogWarning("[玩家城市] 设施卡面库没能生成（多半是拼版图没切片），弹窗骨架已建好，卡面库是空的。");
                library = new Sprite[0];
            }

            var so = new SerializedObject(popup);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("closeButton").objectReferenceValue = closeButton;
            so.FindProperty("dimButton").objectReferenceValue = dimButton;
            so.FindProperty("cityView").objectReferenceValue = cityView;
            so.ApplyModifiedPropertiesWithoutUndo();

            var cso = new SerializedObject(cityView);
            SerializedProperty buttons = cso.FindProperty("slotButtons");
            SerializedProperty images = cso.FindProperty("slotImages");
            SerializedProperty faces = cso.FindProperty("cardFaces");

            buttons.arraySize = slotButtons.Length;
            for (int i = 0; i < slotButtons.Length; i++)
                buttons.GetArrayElementAtIndex(i).objectReferenceValue = slotButtons[i];

            images.arraySize = slotImages.Length;
            for (int i = 0; i < slotImages.Length; i++)
                images.GetArrayElementAtIndex(i).objectReferenceValue = slotImages[i];

            faces.arraySize = library.Length;
            for (int i = 0; i < library.Length; i++)
                faces.GetArrayElementAtIndex(i).objectReferenceValue = library[i];

            cso.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(popup);
            EditorUtility.SetDirty(cityView);
            return library;
        }

        /// <summary>按数字顺序把 CityView 下的格子全找出来（复用路线用：不依赖名字，只看在不在）。</summary>
        private static void CollectSlots(Transform cityView, out Button[] buttons, out Image[] images)
        {
            buttons = new Button[12];
            images = new Image[12];

            int index = 0;
            foreach (Transform child in cityView)
            {
                if (index >= 12) break;

                Button button = child.GetComponent<Button>();
                Image image = child.GetComponent<Image>();
                if (button == null || image == null) continue;

                buttons[index] = button;
                images[index] = image;
                index++;
            }
        }

        /// <summary>把 GameBootstrap 的 Player City Popup 字段接上，返回接了几处。</summary>
        private static int WireGameBootstrap(PlayerCityPopup popup)
        {
            int wired = 0;
            GameBootstrap[] bootstraps = Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include);
            foreach (GameBootstrap bootstrap in bootstraps)
            {
                var so = new SerializedObject(bootstrap);
                SerializedProperty prop = so.FindProperty("playerCityPopup");
                if (prop == null) continue;

                Undo.RecordObject(bootstrap, "接线玩家城市弹窗");
                prop.objectReferenceValue = popup;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(bootstrap);
                wired++;
            }
            return wired;
        }

        private static Canvas FindTargetCanvas()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            if (canvases.Length == 0) return null;

            foreach (Canvas canvas in canvases)
                if (canvas.name == "Canvas" && canvas.isRootCanvas) return canvas;
            foreach (Canvas canvas in canvases)
                if (canvas.isRootCanvas) return canvas;

            return canvases[0];
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(go, parent.gameObject);
            return (RectTransform)go.transform;
        }

        /// <summary>把矩形拉满父物体（left/bottom/right/top 为内缩量）。</summary>
        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            foreach (Transform child in parent)
                if (child.name == name) return child;
            return null;
        }
    }
}

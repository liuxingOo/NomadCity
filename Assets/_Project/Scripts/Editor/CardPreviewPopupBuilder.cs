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
    /// 一键在 Game 场景里生成「卡面放大弹窗」的骨架，并把引用全部接好：宿主 → Root → Dim / Panel / 卡面 / 关闭按钮，
    /// 再把设施卡面库（与设施供应区、城市面板共用）填进去，同时把 GameBootstrap 上的 Card Preview Popup 字段接上。
    ///
    /// 用法：打开 Game 场景 → 菜单 NomadCity / 界面：生成卡面放大弹窗（Card Preview Popup）。
    /// 场景里已经有同名弹窗时会先问一句：重建（删掉重生成，布局最干净）还是复用（保留布局，只重新接线）。
    /// </summary>
    public static class CardPreviewPopupBuilder
    {
        private const string HostName = "CardPreviewPopup";

        [MenuItem("NomadCity/界面：生成卡面放大弹窗（Card Preview Popup）")]
        private static void Build()
        {
            CardPreviewPopup existing = Object.FindFirstObjectByType<CardPreviewPopup>(FindObjectsInactive.Include);
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("卡面放大弹窗已存在",
                    "场景里已经有 CardPreviewPopup。\n\n" +
                    "重建：删掉它，按标准尺寸重新生成（布局最干净）。\n" +
                    "复用接线：保留现有布局，只重新接一遍引用与卡面库。",
                    "重建", "复用接线");

                if (rebuild)
                {
                    Undo.DestroyObjectImmediate(existing.gameObject);
                }
                else
                {
                    Rewire(existing);
                    return;
                }
            }

            Canvas canvas = FindTargetCanvas();
            if (canvas == null)
            {
                Debug.LogError("[卡面放大] 场景里找不到 Canvas，无法生成弹窗。请先打开 Game 场景。");
                return;
            }

            Sprite panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            // —— 宿主 ——
            var host = new GameObject(HostName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(host, "生成卡面放大弹窗");
            GameObjectUtility.SetParentAndAlign(host, canvas.gameObject);
            host.transform.SetAsLastSibling();
            Stretch((RectTransform)host.transform, 0f, 0f, 0f, 0f);
            var popup = host.AddComponent<CardPreviewPopup>();

            // —— Root（弹窗开关）——
            RectTransform root = NewRect("Root", host.transform);
            Stretch(root, 0f, 0f, 0f, 0f);

            // —— Dim（全屏遮罩，挡住底下的地图）——
            RectTransform dim = NewRect("Dim", root);
            Stretch(dim, 0f, 0f, 0f, 0f);
            Image dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.sprite = null;
            dimImage.color = new Color(0f, 0f, 0f, 0.65f);
            dimImage.raycastTarget = true;

            // —— Panel（弹窗背景）——
            RectTransform panel = NewRect("Panel", root);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(480f, 760f);
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = panelSprite;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(0.13f, 0.14f, 0.16f, 1f);
            panelImage.raycastTarget = true;

            // —— 放大卡面（保持宽高比；初始隐藏，由 Show 打开）——
            RectTransform face = NewRect("CardFace", panel);
            face.anchorMin = new Vector2(0.5f, 0.5f);
            face.anchorMax = new Vector2(0.5f, 0.5f);
            face.pivot = new Vector2(0.5f, 0.5f);
            face.anchoredPosition = new Vector2(0f, 25f);
            face.sizeDelta = new Vector2(440f, 623f); // 与切片 600 × 850 同比例，避免拉伸
            Image faceImage = face.gameObject.AddComponent<Image>();
            faceImage.color = Color.white;
            faceImage.preserveAspect = true;
            faceImage.raycastTarget = false;
            faceImage.enabled = false;

            // —— 卡名（可选）——
            RectTransform nameRect = NewRect("CardName", panel);
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.anchoredPosition = new Vector2(0f, 18f);
            nameRect.sizeDelta = new Vector2(-40f, 44f);
            TextMeshProUGUI nameText = nameRect.gameObject.AddComponent<TextMeshProUGUI>();
            nameText.text = string.Empty;
            nameText.fontSize = 30f;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color = Color.white;
            nameText.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) nameText.font = TMP_Settings.defaultFontAsset;

            // —— 关闭按钮（Panel 右上角）——
            RectTransform close = NewRect("CloseButton", panel);
            close.anchorMin = new Vector2(1f, 1f);
            close.anchorMax = new Vector2(1f, 1f);
            close.pivot = new Vector2(1f, 1f);
            close.anchoredPosition = new Vector2(-12f, -12f);
            close.sizeDelta = new Vector2(60f, 60f);
            Image closeImage = close.gameObject.AddComponent<Image>();
            closeImage.sprite = panelSprite;
            closeImage.type = Image.Type.Sliced;
            closeImage.color = new Color(0.85f, 0.86f, 0.88f, 1f);
            closeImage.raycastTarget = true;
            Button closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = closeImage;

            RectTransform closeLabel = NewRect("Text", close);
            Stretch(closeLabel, 0f, 0f, 0f, 0f);
            TextMeshProUGUI closeText = closeLabel.gameObject.AddComponent<TextMeshProUGUI>();
            closeText.text = "关闭";
            closeText.fontSize = 22f;
            closeText.alignment = TextAlignmentOptions.Center;
            closeText.color = new Color(0.12f, 0.12f, 0.14f, 1f);
            closeText.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) closeText.font = TMP_Settings.defaultFontAsset;

            // —— 建设付费按钮（§6.4e 步 1）——
            // 选项一（资源）/ 选项二（金券），平时隐藏，由 ShowForBuild 打开；挂在 Root 下而不是 Panel 里，
            // 免得挤到放大卡面。改第 1 个按钮的位置即可整体挪动这组按钮（第 2 个按钮的 y 相对第 1 个）。
            RectTransform payBox = NewRect("BuildPayButtons", root);
            payBox.anchorMin = new Vector2(0.5f, 0f);
            payBox.anchorMax = new Vector2(0.5f, 0f);
            payBox.pivot = new Vector2(0.5f, 0f);
            payBox.anchoredPosition = new Vector2(0f, 40f);
            payBox.sizeDelta = new Vector2(520f, 120f);

            MakePayButton(payBox, "ResourcePay", new Vector2(0f, 0f), panelSprite);
            MakePayButton(payBox, "VoucherPay", new Vector2(0f, -64f), panelSprite);

            // —— 接线 ——
            Sprite[] library = Wire(popup, root.gameObject, faceImage, nameText, closeButton, closeText, payBox);

            // 场景里默认收起：运行时由 Awake / Show 控制显隐。
            root.gameObject.SetActive(false);

            int wired = WireGameBootstrap(popup);

            EditorSceneManager.MarkSceneDirty(host.scene);
            Selection.activeGameObject = host;
            Debug.Log($"[卡面放大] 已生成弹窗骨架并接好引用（卡面库 {library.Length} 项）。{BootstrapNote(wired)}" +
                      "如需更大的放大图，可直接在场景里调 Panel 与 CardFace 的尺寸（宽度:高度保持 600:850 即可）。");
        }

        /// <summary>场景里已有弹窗时：按名字找回子物体，重新接一遍引用与卡面库（保留现有布局）。</summary>
        private static void Rewire(CardPreviewPopup popup)
        {
            Transform root = FindDeep(popup.transform, "Root");
            Transform face = FindDeep(popup.transform, "CardFace");
            Transform name = FindDeep(popup.transform, "CardName");
            Transform close = FindDeep(popup.transform, "CloseButton");
            Transform closeText = close != null ? FindDeep(close, "Text") : null;
            Transform payBox = FindDeep(popup.transform, "BuildPayButtons");

            Image faceImage = face != null ? face.GetComponent<Image>() : null;
            TextMeshProUGUI nameText = name != null ? name.GetComponent<TextMeshProUGUI>() : null;
            Button closeButton = close != null ? close.GetComponent<Button>() : null;

            if (root == null || faceImage == null || closeButton == null)
            {
                Debug.LogError("[卡面放大] 场景里已有的 CardPreviewPopup 缺 Root / CardFace / CloseButton，没法自动接线。" +
                               "请再执行一次本菜单并选「重建」。");
                return;
            }

            if (payBox == null)
            {
                Debug.LogError("[卡面放大] 这个弹窗是旧版本生成的，里面没有建设用的付费按钮（BuildPayButtons），" +
                               "没法接上「选项一 / 选项二」。请再执行一次本菜单并选「重建」。");
                return;
            }

            Sprite[] library = Wire(popup, root.gameObject, faceImage, nameText, closeButton,
                closeText != null ? closeText.GetComponent<TMP_Text>() : null, payBox);
            int wired = WireGameBootstrap(popup);

            EditorSceneManager.MarkSceneDirty(popup.gameObject.scene);
            Selection.activeGameObject = popup.gameObject;
            Debug.Log($"[卡面放大] 已复用现有弹窗并重新接线（卡面库 {library.Length} 项）。{BootstrapNote(wired)}");
        }

        /// <summary>把弹窗上的引用与卡面库写进去，返回填好的卡面库。</summary>
        private static Sprite[] Wire(CardPreviewPopup popup, GameObject root, Image faceImage,
            TMP_Text nameLabel, Button closeButton, TMP_Text closeLabel, Transform payBox)
        {
            Sprite[] library = CardFaceFiller.BuildFacilityLibrary();
            if (library == null)
            {
                Debug.LogWarning("[卡面放大] 设施卡面库没能生成（多半是拼版图没切片），" +
                                 "弹窗骨架已建好，但卡面库是空的，需要之后再填。");
                library = new Sprite[0];
            }

            Button resourcePayButton = FindButton(payBox, "ResourcePay");
            Button voucherPayButton = FindButton(payBox, "VoucherPay");

            var so = new SerializedObject(popup);
            so.FindProperty("root").objectReferenceValue = root;
            so.FindProperty("cardFaceImage").objectReferenceValue = faceImage;
            so.FindProperty("cardNameLabel").objectReferenceValue = nameLabel;
            so.FindProperty("closeButton").objectReferenceValue = closeButton;
            so.FindProperty("closeLabel").objectReferenceValue = closeLabel;
            so.FindProperty("resourcePayButton").objectReferenceValue = resourcePayButton;
            so.FindProperty("resourcePayLabel").objectReferenceValue = FindText(resourcePayButton);
            so.FindProperty("voucherPayButton").objectReferenceValue = voucherPayButton;
            so.FindProperty("voucherPayLabel").objectReferenceValue = FindText(voucherPayButton);

            SerializedProperty faces = so.FindProperty("cardFaces");
            faces.arraySize = library.Length;
            for (int i = 0; i < library.Length; i++)
                faces.GetArrayElementAtIndex(i).objectReferenceValue = library[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(popup);
            return library;
        }

        /// <summary>在付费按钮容器里摆一个「选项按钮」（背景 + Button + 居中文字）。</summary>
        private static Button MakePayButton(RectTransform parent, string name, Vector2 position, Sprite sprite)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(520f, 52f);

            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.25f, 0.5f, 0.85f, 1f);
            image.raycastTarget = true;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            RectTransform textRect = NewRect("Text", rect);
            Stretch(textRect, 10f, 0f, 10f, 0f);
            TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = name;
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            return button;
        }

        private static Button FindButton(Transform parent, string name)
        {
            if (parent == null) return null;
            Transform found = FindDeep(parent, name);
            return found != null ? found.GetComponent<Button>() : null;
        }

        private static TMP_Text FindText(Button button) =>
            button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;

        /// <summary>把 GameBootstrap 的 Card Preview Popup 字段接上，返回接了几处。</summary>
        private static int WireGameBootstrap(CardPreviewPopup popup)
        {
            int wired = 0;
            GameBootstrap[] bootstraps = Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include);
            foreach (GameBootstrap bootstrap in bootstraps)
            {
                var so = new SerializedObject(bootstrap);
                SerializedProperty prop = so.FindProperty("cardPreviewPopup");
                if (prop == null) continue;

                Undo.RecordObject(bootstrap, "接线卡面放大弹窗");
                prop.objectReferenceValue = popup;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(bootstrap);
                wired++;
            }
            return wired;
        }

        private static string BootstrapNote(int wired) =>
            wired > 0
                ? $"并把 GameBootstrap 的「Card Preview Popup」接上了（{wired} 处）。"
                : "场景里没找到 GameBootstrap，请手动把 CardPreviewPopup 拖到它的「Card Preview Popup」字段。";

        private static Canvas FindTargetCanvas()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            if (canvases.Length == 0) return null;

            // 优先用叫「Canvas」的那个根画布，避免误挂到子画布上。
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
    }
}

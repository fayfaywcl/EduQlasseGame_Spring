using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[ExecuteAlways]
public class BallControl : MonoBehaviour
{
    private const float Gravity = 9.81f;
    private const float PlayerX = -5.25f;
    private const int SignificantFigures = 3;
    private const int MaxBlockTouches = 3;

    private enum GameState
    {
        Running,
        PausedAfterMiss,
        Won,
        Failed
    }

    [Serializable]
    private class SpringPuzzle
    {
        public string label;
        public float springConstant;
        public float compression;
        public float tolerance;
        public Color rewardColor;

        public float TargetMass => springConstant * compression / Gravity;
    }

    private readonly List<SpringPuzzle> puzzles = new()
    {
        new SpringPuzzle { label = "Starter Coil", springConstant = 98.1f, compression = 0.10f, tolerance = 0.08f, rewardColor = new Color(0.20f, 0.70f, 1.00f) },
        new SpringPuzzle { label = "Low Gate", springConstant = 147.15f, compression = 0.20f, tolerance = 0.10f, rewardColor = new Color(1.00f, 0.76f, 0.20f) },
        new SpringPuzzle { label = "High Gate", springConstant = 58.86f, compression = 0.25f, tolerance = 0.09f, rewardColor = new Color(0.33f, 0.93f, 0.54f) },
        new SpringPuzzle { label = "Heavy Drop", springConstant = 196.2f, compression = 0.25f, tolerance = 0.12f, rewardColor = new Color(1.00f, 0.36f, 0.27f) },
        new SpringPuzzle { label = "Final Target", springConstant = 122.625f, compression = 0.32f, tolerance = 0.12f, rewardColor = new Color(0.80f, 0.47f, 1.00f) },
    };

    private readonly Color defaultSkinColor = new(0.93f, 0.96f, 1.00f);

    [SerializeField] private float gameDuration = 60f;
    [SerializeField] private float pauseDuration = 5f;
    [SerializeField] private float scrollSpeed = 1.15f;
    [SerializeField] private float obstacleSpawnX = 7.5f;
    [SerializeField] private float gapHeight = 2.25f;
    [SerializeField] private float minMass = 0.7f;
    [SerializeField] private float maxMass = 6.2f;
    [SerializeField] private float topLaneY = 2.65f;
    [SerializeField] private float bottomLaneY = -2.65f;

    private Camera mainCamera;
    private SpriteRenderer ballRenderer;
    private Sprite blockSprite;
    private Sprite squareSprite;

    private Transform upperBlock;
    private Transform lowerBlock;
    private Transform target;

    private Canvas canvas;
    private Font uiFont;
    private Text timerText;
    private Text progressText;
    private Text energyText;
    private Text promptText;
    private Text statusText;
    private Text rewardText;
    private InputField massInput;
    private GameObject pausePanel;
    private Text pauseTimerText;
    private Text equationTipText;
    private GameObject endPanel;
    private Text endTitleText;
    private Text endBodyText;
    private readonly List<Button> skinButtons = new();

    private readonly List<Color> unlockedSkins = new();
    private GameState state;
    private int currentPuzzleIndex;
    private int solvedCount;
    private int selectedSkinIndex;
    private int blockTouchCount;
    private float timeRemaining;
    private float pauseRemaining;
    private float obstacleX;
    private float selectedMass;
    private float displayedMass;
    private bool hasSubmittedMass;
    private bool showEquationTip;

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (GameObject.Find("Spring Energy UI") == null)
        {
            PrepareSceneObjects();
        }
    }

    private void Start()
    {
        PrepareSceneObjects();

        if (!Application.isPlaying)
        {
            return;
        }

        WireInterfaceEvents();
        StartGame();
    }

    private void PrepareSceneObjects()
    {
        mainCamera = Camera.main;
        ballRenderer = GetComponent<SpriteRenderer>();
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        ConfigureCamera();
        CacheSceneSprites();
        BuildWorld();
        BuildInterface();
    }

    [ContextMenu("Rebuild Spring Energy UI")]
    private void RebuildSpringEnergyUi()
    {
        mainCamera = Camera.main;
        ballRenderer = GetComponent<SpriteRenderer>();
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        ConfigureCamera();
        CacheSceneSprites();
        BuildWorld();
        BuildInterface(true);
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (state == GameState.Running)
        {
            UpdateRunningGame();
            return;
        }

        if (state == GameState.PausedAfterMiss)
        {
            UpdateMissPause();
        }
    }

    private void ConfigureCamera()
    {
        if (mainCamera == null)
        {
            return;
        }

        mainCamera.orthographic = true;
        mainCamera.orthographicSize = 5f;
        mainCamera.backgroundColor = new Color(0.08f, 0.13f, 0.18f);
    }

    private void CacheSceneSprites()
    {
        blockSprite = GameObject.Find("Block_U1")?.GetComponent<SpriteRenderer>()?.sprite;
        squareSprite = GameObject.Find("Square")?.GetComponent<SpriteRenderer>()?.sprite;

        if (blockSprite == null)
        {
            blockSprite = CreateSolidSprite(Color.white);
        }

        if (squareSprite == null)
        {
            squareSprite = blockSprite;
        }
    }

    private void BuildWorld()
    {
        transform.position = new Vector3(PlayerX, 0f, 0f);
        transform.localScale = Vector3.one * 0.82f;

        if (ballRenderer != null)
        {
            ballRenderer.sortingOrder = 8;
            ballRenderer.color = defaultSkinColor;
        }

        CreateBackgroundBand("CeilingBand", new Vector3(0f, 4.72f, 0.5f), new Vector3(18f, 0.22f, 1f), new Color(0.17f, 0.25f, 0.32f));
        CreateBackgroundBand("FloorBand", new Vector3(0f, -4.72f, 0.5f), new Vector3(18f, 0.22f, 1f), new Color(0.17f, 0.25f, 0.32f));
        CreateBackgroundBand("SpringRail", new Vector3(PlayerX, 0f, 0.6f), new Vector3(0.12f, 6.25f, 1f), new Color(0.96f, 0.80f, 0.33f));

        upperBlock = GetOrCreateWorldObject("Block_U1", new Color(0.95f, 0.28f, 0.28f), out _);
        lowerBlock = GetOrCreateWorldObject("Block_B1", new Color(0.95f, 0.28f, 0.28f), out _);
        DeleteWorldObject("TargetCore");
        target = null;
    }

    private void BuildInterface(bool forceRebuild = false)
    {
        GameObject canvasObject = GameObject.Find("Spring Energy UI");
        if (canvasObject == null)
        {
            canvasObject = new GameObject("Spring Energy UI");
        }

        canvas = canvasObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = canvasObject.AddComponent<Canvas>();
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvasObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
        {
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        EnsureEventSystem();

        if (forceRebuild || canvas.transform.childCount == 0)
        {
            ClearChildren(canvas.transform);
            BuildInterfaceContent();
        }

        CacheInterfaceReferences();
        if (Application.isPlaying)
        {
            SetRuntimeInterfaceVisibility();
        }
    }

    private void BuildInterfaceContent()
    {
        GameObject topBar = CreatePanel("TopBar", canvas.transform, new Color(0.04f, 0.07f, 0.10f, 0.82f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(1240f, 72f));
        timerText = CreateText("Timer", topBar.transform, "60.0s", 32, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(210f, 46f));
        progressText = CreateText("Progress", topBar.transform, "Gate 1/5", 27, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(310f, 46f));
        rewardText = CreateText("Rewards", topBar.transform, "Rewards: 0", 24, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(330f, 46f));

        GameObject formulaPanel = CreatePanel("FormulaPanel", canvas.transform, new Color(0.08f, 0.10f, 0.12f, 0.88f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(255f, 112f), new Vector2(470f, 170f));
        promptText = CreateText("Prompt", formulaPanel.transform, "", 24, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(430f, 72f));
        energyText = CreateText("EnergyReadout", formulaPanel.transform, "", 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -88f), new Vector2(430f, 62f));

        massInput = CreateInput(formulaPanel.transform, new Vector2(136f, -52f), new Vector2(180f, 44f));
        CreateButton("SubmitButton", formulaPanel.transform, "Launch", new Vector2(348f, -52f), new Vector2(112f, 44f), new Vector2(0f, 1f), new Vector2(0f, 1f));

        GameObject skinPanel = CreatePanel("SkinPanel", canvas.transform, new Color(0.04f, 0.07f, 0.10f, 0.76f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-245f, 98f), new Vector2(430f, 136f));
        CreateText("SkinTitle", skinPanel.transform, "Avatar rewards", 22, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -23f), new Vector2(300f, 34f));
        for (int i = 0; i <= puzzles.Count; i++)
        {
            int skinIndex = i;
            Button skinButton = CreateButton("Skin" + i, skinPanel.transform, i == 0 ? "D" : "?", new Vector2(46f + i * 66f, -82f), new Vector2(50f, 56f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            skinButton.GetComponent<Image>().color = i == 0 ? defaultSkinColor : new Color(0.18f, 0.20f, 0.22f);
            skinButtons.Add(skinButton);
        }

        statusText = CreateText("Status", canvas.transform, "", 29, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 66f), new Vector2(760f, 58f));

        BuildPausePanel();
        BuildEndPanel();
    }

    private void BuildPausePanel()
    {
        pausePanel = CreatePanel("PausePanel", canvas.transform, new Color(0.02f, 0.03f, 0.04f, 0.86f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 330f));
        CreateText("PauseTitle", pausePanel.transform, "Mass mismatch", 36, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(500f, 54f));
        pauseTimerText = CreateText("PauseTimer", pausePanel.transform, "5.0", 42, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -106f), new Vector2(500f, 58f));
        equationTipText = CreateText("EquationTip", pausePanel.transform, "", 22, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(500f, 80f));

        CreateButton("TipButton", pausePanel.transform, "Tip", new Vector2(-92f, -132f), new Vector2(130f, 48f));

        CreateButton("SkipPauseButton", pausePanel.transform, "Skip", new Vector2(92f, -132f), new Vector2(130f, 48f));

        pausePanel.SetActive(false);
    }

    private void BuildEndPanel()
    {
        endPanel = CreatePanel("EndPanel", canvas.transform, new Color(0.02f, 0.03f, 0.04f, 0.88f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(670f, 360f));
        endTitleText = CreateText("EndTitle", endPanel.transform, "", 42, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(610f, 64f));
        endBodyText = CreateText("EndBody", endPanel.transform, "", 23, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -126f), new Vector2(610f, 140f));

        CreateButton("RestartButton", endPanel.transform, "Restart", new Vector2(0f, -300f), new Vector2(150f, 52f));

        endPanel.SetActive(false);
    }

    private bool HasRequiredInterface(Transform root)
    {
        bool hasCoreUi = root.Find("TopBar/Timer") != null
            && root.Find("TopBar/Progress") != null
            && root.Find("TopBar/Rewards") != null
            && root.Find("FormulaPanel/Prompt") != null
            && root.Find("FormulaPanel/EnergyReadout") != null
            && root.Find("FormulaPanel/MassInput") != null
            && root.Find("FormulaPanel/SubmitButton") != null
            && root.Find("SkinPanel/Skin0") != null
            && root.Find("Status") != null
            && root.Find("PausePanel/PauseTimer") != null
            && root.Find("PausePanel/EquationTip") != null
            && root.Find("PausePanel/TipButton") != null
            && root.Find("PausePanel/SkipPauseButton") != null
            && root.Find("EndPanel/EndTitle") != null
            && root.Find("EndPanel/EndBody") != null
            && root.Find("EndPanel/RestartButton") != null;

        if (!hasCoreUi)
        {
            return false;
        }

        for (int i = 0; i <= puzzles.Count; i++)
        {
            if (root.Find("SkinPanel/Skin" + i) == null)
            {
                return false;
            }
        }

        return true;
    }

    private void CacheInterfaceReferences()
    {
        Transform root = canvas.transform;
        timerText = GetSceneComponent<Text>(root, "TopBar/Timer");
        progressText = GetSceneComponent<Text>(root, "TopBar/Progress");
        rewardText = GetSceneComponent<Text>(root, "TopBar/Rewards");
        promptText = GetSceneComponent<Text>(root, "FormulaPanel/Prompt");
        energyText = GetSceneComponent<Text>(root, "FormulaPanel/EnergyReadout");
        massInput = GetSceneComponent<InputField>(root, "FormulaPanel/MassInput");
        statusText = GetSceneComponent<Text>(root, "Status");

        Transform pauseTransform = root.Find("PausePanel");
        pausePanel = pauseTransform == null ? null : pauseTransform.gameObject;
        pauseTimerText = GetSceneComponent<Text>(root, "PausePanel/PauseTimer");
        equationTipText = GetSceneComponent<Text>(root, "PausePanel/EquationTip");

        Transform endTransform = root.Find("EndPanel");
        endPanel = endTransform == null ? null : endTransform.gameObject;
        endTitleText = GetSceneComponent<Text>(root, "EndPanel/EndTitle");
        endBodyText = GetSceneComponent<Text>(root, "EndPanel/EndBody");
        ConfigureEndPanelLayout(root);

        skinButtons.Clear();
        for (int i = 0; i <= puzzles.Count; i++)
        {
            Button skinButton = GetSceneComponent<Button>(root, "SkinPanel/Skin" + i);
            if (skinButton != null)
            {
                skinButtons.Add(skinButton);
            }
        }
    }

    private T GetSceneComponent<T>(Transform root, string path) where T : Component
    {
        Transform child = root.Find(path);
        return child == null ? null : child.GetComponent<T>();
    }

    private void WireInterfaceEvents()
    {
        Button submitButton = GetSceneComponent<Button>(canvas.transform, "FormulaPanel/SubmitButton");
        if (submitButton != null)
        {
            submitButton.onClick.RemoveAllListeners();
            submitButton.onClick.AddListener(SubmitMass);
        }

        Button tipButton = GetSceneComponent<Button>(canvas.transform, "PausePanel/TipButton");
        if (tipButton != null)
        {
            tipButton.onClick.RemoveAllListeners();
            tipButton.onClick.AddListener(ToggleEquationTip);
        }

        Button skipButton = GetSceneComponent<Button>(canvas.transform, "PausePanel/SkipPauseButton");
        if (skipButton != null)
        {
            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(ResumeAfterMiss);
        }

        Button restartButton = GetSceneComponent<Button>(canvas.transform, "EndPanel/RestartButton");
        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(StartGame);
        }

        for (int i = 0; i < skinButtons.Count; i++)
        {
            int skinIndex = i;
            skinButtons[i].onClick.RemoveAllListeners();
            skinButtons[i].onClick.AddListener(() => SelectSkin(skinIndex));
        }
    }

    private void SetRuntimeInterfaceVisibility()
    {
        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }

        if (endPanel != null)
        {
            endPanel.SetActive(false);
        }

        if (timerText != null)
        {
            timerText.text = FormatSeconds(gameDuration);
        }

        if (progressText != null)
        {
            progressText.text = "Gate 1/" + puzzles.Count;
        }

        if (rewardText != null)
        {
            rewardText.text = "Rewards: 0 | Touches: 0/" + MaxBlockTouches;
        }
    }

    private void ConfigureEndPanelLayout(Transform root)
    {
        if (endPanel != null)
        {
            RectTransform endRect = endPanel.GetComponent<RectTransform>();
            if (endRect != null)
            {
                endRect.sizeDelta = new Vector2(670f, 360f);
            }
        }

        ConfigureRect(root.Find("EndPanel/EndTitle"), new Vector2(0f, -54f), new Vector2(610f, 64f));
        ConfigureRect(root.Find("EndPanel/EndBody"), new Vector2(0f, -126f), new Vector2(610f, 140f));
        ConfigureRect(root.Find("EndPanel/RestartButton"), new Vector2(0f, -300f), new Vector2(150f, 52f));

        if (endBodyText != null)
        {
            endBodyText.fontSize = 23;
            endBodyText.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }

    private void ConfigureRect(Transform transformToConfigure, Vector2 position, Vector2 size)
    {
        if (transformToConfigure == null)
        {
            return;
        }

        RectTransform rect = transformToConfigure.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private void StartGame()
    {
        currentPuzzleIndex = 0;
        solvedCount = 0;
        selectedSkinIndex = 0;
        blockTouchCount = 0;
        timeRemaining = gameDuration;
        state = GameState.Running;
        pausePanel.SetActive(false);
        endPanel.SetActive(false);
        unlockedSkins.Clear();
        unlockedSkins.Add(defaultSkinColor);
        SelectSkin(0);
        LoadPuzzle();
        UpdateSkinButtons();
        SetStatus("Test mode: solve with kx = mg. Enter mass correct to 3 s.f.");
    }

    private void UpdateRunningGame()
    {
        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
        {
            FailGame();
            return;
        }

        obstacleX -= scrollSpeed * Time.deltaTime;
        PlaceObstaclePair();
        MoveBall();
        UpdateReadouts();

        if (obstacleX <= PlayerX + 0.28f)
        {
            if (HasCorrectMass())
            {
                PassGate();
            }
            else
            {
                MissGate();
            }
        }
    }

    private void UpdateMissPause()
    {
        pauseRemaining -= Time.unscaledDeltaTime;
        pauseTimerText.text = FormatSeconds(pauseRemaining);

        if (pauseRemaining <= 0f)
        {
            ResumeAfterMiss();
        }
    }

    private void SubmitMass()
    {
        if (state != GameState.Running)
        {
            return;
        }

        if (!float.TryParse(massInput.text, out float enteredMass))
        {
            SetStatus("Enter the ball mass in kg before the block reaches you.");
            return;
        }

        selectedMass = Mathf.Clamp(enteredMass, minMass, maxMass);
        hasSubmittedMass = true;
        SetStatus("Mass submitted: " + FormatSignificantFigures(selectedMass) + " kg. Checked to 3 s.f.");
        massInput.text = FormatSignificantFigures(selectedMass);
    }

    private void ToggleEquationTip()
    {
        showEquationTip = !showEquationTip;
        equationTipText.text = showEquationTip
            ? "Spring force equals weight at balance:\nkx = mg, so m = kx / g. Answer to 3 s.f."
            : "Use Tip for the method, or Skip to try again now.";
    }

    private void ResumeAfterMiss()
    {
        if (state != GameState.PausedAfterMiss)
        {
            return;
        }

        pausePanel.SetActive(false);
        state = GameState.Running;
        LoadPuzzle();
        SetStatus("Try the same block again. Use kx = mg and answer to 3 s.f.");
    }

    private void SelectSkin(int skinIndex)
    {
        if (skinIndex < 0 || skinIndex >= unlockedSkins.Count)
        {
            SetStatus("That avatar color is still locked. Solve more gates to unlock it.");
            return;
        }

        selectedSkinIndex = skinIndex;
        if (ballRenderer != null)
        {
            ballRenderer.color = unlockedSkins[selectedSkinIndex];
        }

        UpdateSkinButtons();
    }

    private void LoadPuzzle()
    {
        SpringPuzzle puzzle = puzzles[currentPuzzleIndex];
        obstacleX = obstacleSpawnX;
        hasSubmittedMass = false;
        selectedMass = Mathf.Clamp((minMass + maxMass) * 0.5f, minMass, maxMass);
        displayedMass = selectedMass;
        massInput.text = "";
        massInput.Select();
        showEquationTip = false;

        promptText.text = puzzle.label + "\nTest: find m to 3 s.f.  \n k = " + FormatGivenValue(puzzle.springConstant) + " N/m, x = " + FormatGivenValue(puzzle.compression) + " m";
        equationTipText.text = "Use Tip for the method, or Skip to try again now.";
        PlaceObstaclePair();
        UpdateReadouts();
    }

    private bool HasCorrectMass()
    {
        if (!hasSubmittedMass)
        {
            return false;
        }

        SpringPuzzle puzzle = puzzles[currentPuzzleIndex];
        return Mathf.Abs(RoundToSignificantFigures(selectedMass) - RoundToSignificantFigures(puzzle.TargetMass)) <= 0.0001f;
    }

    private void PassGate()
    {
        SpringPuzzle puzzle = puzzles[currentPuzzleIndex];
        solvedCount++;
        UnlockRewardColor(puzzle.rewardColor);

        if (currentPuzzleIndex >= puzzles.Count - 1)
        {
            WinGame();
            return;
        }

        currentPuzzleIndex++;
        LoadPuzzle();
        SetStatus("Correct to 3 s.f. Reward unlocked, next spring is incoming.");
    }

    private void MissGate()
    {
        blockTouchCount++;
        SpringPuzzle puzzle = puzzles[currentPuzzleIndex];
        Debug.Log("Block touch " + blockTouchCount + "/" + MaxBlockTouches + " on " + puzzle.label + ". " + BuildCalculationExplanation(puzzle)
            + " Submitted mass: " + (hasSubmittedMass ? FormatSignificantFigures(selectedMass) + " kg." : "none."));

        if (blockTouchCount >= MaxBlockTouches)
        {
            FailGame("You touched a block " + MaxBlockTouches + " times.");
            return;
        }

        state = GameState.PausedAfterMiss;
        pauseRemaining = pauseDuration;
        pausePanel.SetActive(true);
        pauseTimerText.text = FormatSeconds(pauseDuration);

        equationTipText.text = "Block touch " + blockTouchCount + "/" + MaxBlockTouches + ".\n Use kx = mg, then retry.";
        //Answer logged in Console.
        SetStatus("Incorrect mass. No answer shown here; check Console after the test.");
    }

    private void WinGame()
    {
        state = GameState.Won;
        if (target != null)
        {
            target.gameObject.SetActive(false);
        }
        endPanel.SetActive(true);
        endTitleText.text = "Test complete";
        endBodyText.text = "You passed all " + puzzles.Count + " spring-energy gates with " + FormatSeconds(timeRemaining) + " left.";
        SetStatus("Win. All rewards are available for your avatar.");
    }

    private void FailGame()
    {
        FailGame("The " + gameDuration.ToString("0") + " second limit ran out.");
    }

    private void FailGame(string reason)
    {
        state = GameState.Failed;
        timeRemaining = 0f;
        endPanel.SetActive(true);
        SpringPuzzle puzzle = puzzles[Mathf.Clamp(currentPuzzleIndex, 0, puzzles.Count - 1)];
        endTitleText.text = "Test failed";
        endBodyText.text = reason + "\nSolved " + solvedCount + " of " + puzzles.Count + " gates.\n" + BuildCalculationExplanation(puzzle);
        SetStatus("Fail. Review the final calculation and restart the test.");
        UpdateReadouts();
    }

    private void UnlockRewardColor(Color color)
    {
        if (unlockedSkins.Count <= puzzles.Count)
        {
            unlockedSkins.Add(color);
        }

        if (rewardText != null)
        {
            rewardText.text = "Rewards: " + Mathf.Max(0, unlockedSkins.Count - 1) + " | Touches: " + blockTouchCount + "/" + MaxBlockTouches;
        }
        UpdateSkinButtons();
    }

    private void MoveBall()
    {
        displayedMass = Mathf.Lerp(displayedMass, selectedMass, 8f * Time.deltaTime);
        float targetY = MassToY(displayedMass);
        transform.position = Vector3.Lerp(transform.position, new Vector3(PlayerX, targetY, 0f), 8.5f * Time.deltaTime);
        transform.Rotate(0f, 0f, -280f * Time.deltaTime);
    }

    private void PlaceObstaclePair()
    {
        SpringPuzzle puzzle = puzzles[currentPuzzleIndex];
        float gapCenterY = MassToY(puzzle.TargetMass);
        float topHeight = Mathf.Max(0.6f, 4.65f - (gapCenterY + gapHeight * 0.5f));
        float bottomHeight = Mathf.Max(0.6f, gapCenterY - gapHeight * 0.5f + 4.65f);

        upperBlock.position = new Vector3(obstacleX, gapCenterY + gapHeight * 0.5f + topHeight * 0.5f, 0f);
        upperBlock.localScale = new Vector3(0.92f, topHeight, 1f);
        lowerBlock.position = new Vector3(obstacleX, -4.65f + bottomHeight * 0.5f, 0f);
        lowerBlock.localScale = new Vector3(0.92f, bottomHeight, 1f);

        if (target != null)
        {
            target.gameObject.SetActive(false);
        }
    }

    private void UpdateReadouts()
    {
        SpringPuzzle puzzle = puzzles[currentPuzzleIndex];
        float elasticEnergy = 0.5f * puzzle.springConstant * puzzle.compression * puzzle.compression;
        float potentialEnergy = selectedMass * Gravity * Mathf.Max(0f, transform.position.y + 3f);
        timerText.text = FormatSeconds(timeRemaining);
        progressText.text = "Gate " + (currentPuzzleIndex + 1) + "/" + puzzles.Count;
        if (rewardText != null)
        {
            rewardText.text = "Rewards: " + Mathf.Max(0, unlockedSkins.Count - 1) + " | Touches: " + blockTouchCount + "/" + MaxBlockTouches;
        }
        energyText.text = "E spring = 1/2kx^2 = " + elasticEnergy.ToString("0.00") + " J\nPE now = mgh = " + potentialEnergy.ToString("0.00") + " J";
    }

    private string BuildCalculationExplanation(SpringPuzzle puzzle)
    {
        return "Ans: \n Use kx = mg, so m = kx / g = (" + FormatGivenValue(puzzle.springConstant) + " N/m x "
            + FormatGivenValue(puzzle.compression) + " m) / " + FormatGivenValue(Gravity)
            + " m/s^2 = " + FormatSignificantFigures(puzzle.TargetMass) + " kg (3 s.f.).";
    }

    private string FormatGivenValue(float value)
    {
        return value.ToString("0.###");
    }

    private string FormatSeconds(float seconds)
    {
        return Mathf.CeilToInt(Mathf.Max(0f, seconds)).ToString() + "s";
    }

    private float RoundToSignificantFigures(float value)
    {
        float absoluteValue = Mathf.Abs(value);
        if (absoluteValue <= Mathf.Epsilon)
        {
            return 0f;
        }

        float magnitude = Mathf.Floor(Mathf.Log10(absoluteValue));
        float scale = Mathf.Pow(10f, SignificantFigures - 1 - magnitude);
        return Mathf.Round(value * scale) / scale;
    }

    private string FormatSignificantFigures(float value)
    {
        float rounded = RoundToSignificantFigures(value);
        float absoluteValue = Mathf.Abs(rounded);
        if (absoluteValue <= Mathf.Epsilon)
        {
            return "0." + new string('0', SignificantFigures - 1);
        }

        int magnitude = Mathf.FloorToInt(Mathf.Log10(absoluteValue));
        int decimals = Mathf.Max(0, SignificantFigures - magnitude - 1);
        return rounded.ToString("F" + decimals);
    }

    private float MassToY(float mass)
    {
        float t = Mathf.InverseLerp(minMass, maxMass, Mathf.Clamp(mass, minMass, maxMass));
        return Mathf.Lerp(topLaneY, bottomLaneY, t);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void UpdateSkinButtons()
    {
        for (int i = 0; i < skinButtons.Count; i++)
        {
            Button button = skinButtons[i];
            bool unlocked = i < unlockedSkins.Count;
            button.interactable = unlocked;
            button.GetComponentInChildren<Text>().text = unlocked ? (i == 0 ? "D" : i.ToString()) : "?";
            button.GetComponent<Image>().color = unlocked ? unlockedSkins[i] : new Color(0.18f, 0.20f, 0.22f);
            button.transform.localScale = i == selectedSkinIndex ? Vector3.one * 1.12f : Vector3.one;
        }
    }

    private Transform GetOrCreateWorldObject(string objectName, Color color, out SpriteRenderer spriteRenderer)
    {
        GameObject worldObject = GameObject.Find(objectName);
        if (worldObject == null)
        {
            worldObject = new GameObject(objectName);
        }

        spriteRenderer = worldObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = worldObject.AddComponent<SpriteRenderer>();
        }

        spriteRenderer.sprite = objectName == "TargetCore" ? squareSprite : blockSprite;
        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = objectName == "TargetCore" ? 5 : 4;
        return worldObject.transform;
    }

    private void DeleteWorldObject(string objectName)
    {
        GameObject worldObject = GameObject.Find(objectName);
        if (worldObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(worldObject);
        }
        else
        {
            DestroyImmediate(worldObject);
        }
    }

    private void CreateBackgroundBand(string objectName, Vector3 position, Vector3 scale, Color color)
    {
        SpriteRenderer spriteRenderer;
        Transform band = GetOrCreateWorldObject(objectName, color, out spriteRenderer);
        spriteRenderer.sprite = squareSprite;
        spriteRenderer.sortingOrder = -2;
        band.position = position;
        band.localScale = scale;
    }

    private Sprite CreateSolidSprite(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    }

    private GameObject CreatePanel(string objectName, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(objectName);
        panel.transform.SetParent(parent, false);
        Image image = panel.AddComponent<Image>();
        image.color = color;
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return panel;
    }

    private Text CreateText(string objectName, Transform parent, string text, int fontSize, TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);
        Text label = textObject.AddComponent<Text>();
        label.font = uiFont;
        label.text = text;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = alignment;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return label;
    }

    private InputField CreateInput(Transform parent, Vector2 position, Vector2 size)
    {
        GameObject inputObject = CreatePanel("MassInput", parent, new Color(0.94f, 0.96f, 0.98f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
        InputField input = inputObject.AddComponent<InputField>();
        Text text = CreateText("Text", inputObject.transform, "", 22, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-22f, -8f));
        text.color = new Color(0.04f, 0.06f, 0.08f);
        Text placeholder = CreateText("Placeholder", inputObject.transform, "kg", 22, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-22f, -8f));
        placeholder.color = new Color(0.43f, 0.48f, 0.53f);
        input.textComponent = text;
        input.placeholder = placeholder;
        input.contentType = InputField.ContentType.DecimalNumber;
        return input;
    }

    private Button CreateButton(string objectName, Transform parent, string label, Vector2 position, Vector2 size)
    {
        return CreateButton(objectName, parent, label, position, size, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
    }

    private Button CreateButton(string objectName, Transform parent, string label, Vector2 position, Vector2 size, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject buttonObject = CreatePanel(objectName, parent, new Color(0.16f, 0.46f, 0.77f, 1f), anchorMin, anchorMax, position, size);
        Button button = buttonObject.AddComponent<Button>();
        Text buttonText = CreateText("Label", buttonObject.transform, label, 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        buttonText.horizontalOverflow = HorizontalWrapMode.Overflow;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.24f, 0.57f, 0.90f);
        colors.pressedColor = new Color(0.08f, 0.32f, 0.62f);
        colors.disabledColor = new Color(0.20f, 0.22f, 0.24f, 0.75f);
        button.colors = colors;
        return button;
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }
}

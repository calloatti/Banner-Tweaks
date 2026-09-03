using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.CoreUI;
using Timberborn.DecalSystem;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.UISound;
using UnityEngine;
using UnityEngine.UIElements;

namespace Calloatti.BannerTweaks
{
  public class DecalGridPanel : IPanelController
  {
    internal static DecalGridPanel Instance;
    internal bool IsShowing => _panelStack.IsPanelOnTop(this);
    private const bool _debugMode = false;
    private readonly PanelStack _panelStack;
    private readonly IDecalService _decalService;
    private readonly EventBus _eventBus;
    private readonly UISoundController _uiSoundController;
    private readonly ILoc _loc;
    private DecalSupplier _supplier;
    private VisualElement _root;
    private VisualElement _container1;
    private VisualElement _container2;
    private VisualElement _container3;
    private Button _topTitle;
    private Button _bottomTitle;
    private Button _pageInfoLabel;
    private Button _topButton;
    private Button _prevButton;
    private Button _nextButton;
    private readonly List<VisualElement> _decalElements = new();
    private readonly List<Decal> _cachedDecals = new();

    private readonly DecalGroupStore _decalGroupStore;
    private readonly Dictionary<string, string> _lastGroupIdByCategory = new();
    private GroupNode _currentGroupNode;
    private int _currentPage;
    private float _lastWheelTime;
    private Decal _originalDecal; // snapshot of active decal when panel opened
    private VisualElement _upFolderElement;
    private VisualElement _originalDecalElement; // slot48 - never changes
    private VisualElement _currentDecalElement; // slot49 - updates on click
    private EventHandler _activeDecalHandler;

    private static readonly Color SelectedTint = new Color(1f, 1f, 1f, 1f);
    private static readonly Color UnselectedTint = new Color(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Color FrameSelected = new Color(0.8f, 0.6f, 0.1f, 1f);
    private static readonly Color FrameNormal = new Color(54 / 255f, 48 / 255f, 35 / 255f, 1f);

    private static readonly int FontSize1 = 12;
    private const int CellSize = 96;
    private const int Gap = 6;
    private const int Cols = 10;
    private const int ItemRows = 5;
    private const int NormalSlots = Cols * ItemRows - 3;
    private const int SlotBorderWidth = 2;

    private const string TopKey = "Calloatti.BannerTweaks.Top";
    private const string PrevKey = "Calloatti.BannerTweaks.Prev";
    private const string NextKey = "Calloatti.BannerTweaks.Next";
    private const string OriginalKey = "Calloatti.BannerTweaks.Original";
    private const string CurrentKey = "Calloatti.BannerTweaks.Current";
    private const string FolderTagKey = "Calloatti.BannerTweaks.FolderTag";
    private const string ModTagKey = "Calloatti.BannerTweaks.ModTag";
    private const string GroupTagKey = "Calloatti.BannerTweaks.GroupTag";

    public DecalGridPanel(
        PanelStack panelStack,
        IDecalService decalService,
        EventBus eventBus,
        UISoundController uiSoundController,
        ILoc loc,
        DecalGroupStore decalGroupStore)
    {
      Instance = this;
      _panelStack = panelStack;
      _decalService = decalService;
      _eventBus = eventBus;
      _uiSoundController = uiSoundController;
      _loc = loc;
      _decalGroupStore = decalGroupStore;
    }

    public void Show(DecalSupplier supplier)
    {
      if (IsShowing)
        return;

      if (_supplier != null)
      {
        if (_activeDecalHandler != null)
          _supplier.ActiveDecalChanged -= _activeDecalHandler;
        SaveGroupState();
      }

      _supplier = supplier;
      _originalDecal = supplier.ActiveDecal;
      CacheDecals();
      _activeDecalHandler = (s, e) =>
      {
        UpdateCurrentDecalSlot();
        RefreshSelectionVisuals();
      };
      supplier.ActiveDecalChanged += _activeDecalHandler;

      var groups = _decalGroupStore.GetGroupsForCategory(supplier.Category);
      if (_lastGroupIdByCategory.TryGetValue(supplier.Category, out var lastGroupId))
        _currentGroupNode = FindGroup(groups, lastGroupId) ?? groups.FirstOrDefault();
      else
        _currentGroupNode = groups.FirstOrDefault();

      _panelStack.Push(this, hideTop: false, showOverlay: false, isDialog: false, lockSpeed: false);
      _eventBus.Register(this);
    }

    private static GroupNode FindGroup(List<GroupNode> roots, string groupId)
    {
      foreach (var root in roots)
      {
        var result = FindGroup(root, groupId);
        if (result != null)
          return result;
      }
      return null;
    }

    private static GroupNode FindGroup(GroupNode node, string groupId)
    {
      if (node.Id == groupId)
        return node;
      foreach (var child in node.Children)
      {
        var result = FindGroup(child, groupId);
        if (result != null)
          return result;
      }
      return null;
    }

    public VisualElement GetPanel()
    {
      if (_root == null)
        BuildUI();
      Populate();
      return _root;
    }

    private void BuildUI()
    {
      int gridWidth = Cols * CellSize + Cols * Gap + 24;

      //var box = new NineSliceVisualElement();
      //box.AddToClassList("entity-sub-panel");
      //box.AddToClassList("bg-sub-box--green");
      var box = new VisualElement();
      box.style.flexDirection = FlexDirection.Column;
      box.style.marginLeft = StyleKeyword.Auto;
      box.style.marginRight = StyleKeyword.Auto;
      box.style.paddingTop = 0;
      box.style.paddingBottom = 0;
      box.style.paddingLeft = 0;
      box.style.paddingRight = 0;
      box.style.backgroundColor = new Color(34 / 255f, 54 / 255f, 44 / 255f, 1f);
      box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = new StyleColor(new Color(166 / 255f, 143 / 255f, 97 / 255f, 1f));
      box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 2;
      box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = box.style.borderTopLeftRadius = box.style.borderTopRightRadius = 4;

      _container1 = new VisualElement();
      _container1.style.flexDirection = FlexDirection.Row;
      _container1.style.flexWrap = Wrap.Wrap;
      _container1.style.width = gridWidth;
      _container1.style.flexShrink = 0;

      _topTitle = CreateBarControl(_supplier?.Category ?? "", 10 * CellSize + 9 * Gap, TextAnchor.MiddleLeft, () =>
      {
        if (_currentGroupNode?.Parent != null)
          _currentGroupNode = _currentGroupNode.Parent;
        _currentPage = 0;
        Populate();
      });

      _topTitle.style.borderLeftColor = _topTitle.style.borderRightColor = _topTitle.style.borderTopColor = _topTitle.style.borderBottomColor = new StyleColor(new Color(166 / 255f, 143 / 255f, 97 / 255f, 0f));
      _topTitle.style.backgroundColor = new Color(21 / 255f, 39 / 255f, 34 / 255f, 0f);

      _topTitle.style.fontSize = 14;

      _container1.Add(_topTitle);

      box.Add(_container1);

      var sep1 = new VisualElement();
      sep1.style.height = 2;
      sep1.style.width = gridWidth;
      sep1.style.backgroundColor = new Color(166 / 255f, 143 / 255f, 97 / 255f, 1f);
      sep1.style.marginLeft = 0;
      sep1.style.marginRight = 0;
      sep1.style.marginTop = 0;
      sep1.style.marginBottom = 0;
      sep1.style.flexShrink = 0;
      box.Add(sep1);

      _container2 = new VisualElement();
      _container2.style.flexDirection = FlexDirection.Row;
      _container2.style.flexWrap = Wrap.Wrap;
      _container2.style.width = gridWidth;
      _container2.style.flexShrink = 0;

      box.Add(_container2);

      var sep2 = new VisualElement();
      sep2.style.height = 2;
      sep2.style.width = gridWidth;
      sep2.style.backgroundColor = new Color(166 / 255f, 143 / 255f, 97 / 255f, 1f);
      sep2.style.marginLeft = 0;
      sep2.style.marginRight = 0;
      sep2.style.marginTop = 0;
      sep2.style.marginBottom = 0;
      sep2.style.flexShrink = 0;
      box.Add(sep2);

      _container3 = new VisualElement();
      _container3.style.flexDirection = FlexDirection.Row;
      _container3.style.width = gridWidth;
      _container3.style.flexShrink = 0;
      _container3.style.flexWrap = Wrap.Wrap;

      _container1.style.paddingTop = 12;
      _container1.style.paddingBottom = 12;
      _container1.style.paddingLeft = 12;
      _container1.style.paddingRight = 12;
      _container2.style.paddingTop = 12;
      _container2.style.paddingBottom = 12;
      _container2.style.paddingLeft = 12;
      _container2.style.paddingRight = 12;
      _container3.style.paddingTop = 12;
      _container3.style.paddingBottom = 12;
      _container3.style.paddingLeft = 12;
      _container3.style.paddingRight = 12;


      // Bottom title slot (7 slots wide)
      _bottomTitle = CreateBarControl("", 6 * CellSize + 5 * Gap, TextAnchor.MiddleLeft);

      _bottomTitle.style.borderLeftColor = _bottomTitle.style.borderRightColor = _bottomTitle.style.borderTopColor = _bottomTitle.style.borderBottomColor = new StyleColor(new Color(166 / 255f, 143 / 255f, 97 / 255f, 0f));
      _bottomTitle.style.backgroundColor = new Color(21 / 255f, 39 / 255f, 34 / 255f, 0f);

      _bottomTitle.style.fontSize = 14;

      _container3.Add(_bottomTitle);

      _pageInfoLabel = CreateBarControl($" {0} - 1/1 ", CellSize, TextAnchor.MiddleCenter);

      _container3.Add(_pageInfoLabel);

      _topButton = CreateBarControl(_loc.T(TopKey), CellSize, TextAnchor.MiddleCenter, () =>
      {
        if (_currentPage > 0)
        {
          _currentPage = 0;
          Populate();
        }
      });
      _container3.Add(_topButton);

      _prevButton = CreateBarControl(_loc.T(PrevKey), CellSize, TextAnchor.MiddleCenter, () =>
      {
        if (_currentPage > 0)
        {
          _currentPage--;
          Populate();
        }
      });
      _container3.Add(_prevButton);

      _nextButton = CreateBarControl(_loc.T(NextKey), CellSize, TextAnchor.MiddleCenter, () => { _currentPage++; Populate(); });
      _container3.Add(_nextButton);

      box.Add(_container3);

      var closeButton = new Button();
      closeButton.AddToClassList("close-button");
      closeButton.style.top = 6;
      closeButton.style.right = 6;
      closeButton.RegisterCallback<ClickEvent>(evt => Close());
      box.Add(closeButton);

      _root = box;

      _root.RegisterCallback<WheelEvent>(evt =>
      {
        if (Time.realtimeSinceStartup - _lastWheelTime < 0.3f)
          return;
        _lastWheelTime = Time.realtimeSinceStartup;
        if (evt.delta.y > 0 && _currentPage < ComputeTotalPages() - 1)
        {
          _currentPage++;
          Populate();
        }
        else if (evt.delta.y < 0 && _currentPage > 0)
        {
          _currentPage--;
          Populate();
        }
      });

    }

    [OnEvent]
    public void OnDecalsReloaded(DecalsReloadedEvent e)
    {
      if (IsShowing)
      {
        CacheDecals();
        Populate();
      }
    }

    private void CacheDecals()
    {
      _cachedDecals.Clear();
      _cachedDecals.AddRange(_decalService.GetDecals(_supplier.Category));
    }

    private IEnumerable<Decal> GetDecalsForGroup(GroupNode group)
    {
      var idSet = new HashSet<string>(group.DecalIds);
      return _cachedDecals.Where(d => idSet.Contains(d.Id));
    }

    private void Populate()
    {
      _decalElements.Clear();
      _container2.Clear();

      if (_supplier == null)
        return;

      var groups = _decalGroupStore.GetGroupsForCategory(_supplier.Category);
      if (_currentGroupNode == null || !groups.Contains(_currentGroupNode) && !groups.Any(g => ContainsDescendant(g, _currentGroupNode)))
      {
        _currentGroupNode = groups.FirstOrDefault();
        _currentPage = 0;
      }

      _topTitle.text = _currentGroupNode != null && !string.IsNullOrEmpty(_currentGroupNode.Id)
          ? _supplier.Category + "\\" + _currentGroupNode.Id
          : _supplier.Category;

      // Add ".." up-folder element
      _upFolderElement = CreateFolderElement("..", "..", () =>
      {
        if (_currentGroupNode?.Parent != null)
          _currentGroupNode = _currentGroupNode.Parent;
        _currentPage = 0;
        Populate();
      });
      _decalElements.Add(_upFolderElement);

      // Child group elements
      if (_currentGroupNode != null)
      {
        foreach (var child in _currentGroupNode.Children)
        {
          var capturedChild = child;
          _decalElements.Add(CreateFolderElement(child.DisplayName, child.Id, () =>
          {
            _currentGroupNode = capturedChild;
            _currentPage = 0;
            Populate();
          }, capturedChild));
        }

        // Decal elements for the current group
        foreach (var decal in GetDecalsForGroup(_currentGroupNode))
          _decalElements.Add(CreateDecalElement(decal));
      }

      int totalItems = _decalElements.Count - 1;
      int totalPages = ComputeTotalPages();
      _currentPage = Mathf.Clamp(_currentPage, 0, totalPages - 1);

      _container2.Add(_decalElements[0]);

      int start = _currentPage * NormalSlots + 1;
      int end = Mathf.Min(start + NormalSlots, totalItems + 1);
      for (int i = start; i < end; i++)
        _container2.Add(_decalElements[i]);
      for (int i = end - start; i < NormalSlots; i++)
      {
        var empty = new VisualElement();
        empty.style.width = CellSize;
        empty.style.height = CellSize;
        empty.style.marginLeft = 3;
        empty.style.marginRight = 3;
        empty.style.marginTop = 3;
        empty.style.marginBottom = 3;
        empty.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 0.6f);
        empty.style.borderTopWidth = empty.style.borderBottomWidth = empty.style.borderLeftWidth = empty.style.borderRightWidth = SlotBorderWidth;
        empty.style.borderTopColor = empty.style.borderBottomColor = empty.style.borderLeftColor = empty.style.borderRightColor = new StyleColor(FrameNormal);
        _container2.Add(empty);
      }

      _originalDecalElement = CreateDecalElement(_originalDecal);
      AddSlotLabel(_originalDecalElement, _loc.T(OriginalKey));
      _decalElements.Add(_originalDecalElement);
      _container2.Add(_originalDecalElement);

      var activeDecal = _supplier?.ActiveDecal ?? default;
      _currentDecalElement = CreateDecalElement(activeDecal);

      _currentDecalElement.pickingMode = PickingMode.Ignore;

      AddSlotLabel(_currentDecalElement, _loc.T(CurrentKey));
      _decalElements.Add(_currentDecalElement);
      _container2.Add(_currentDecalElement);

      int totalDecals = 0;
      foreach (var el in _decalElements)
        if (el.userData is Decal)
          totalDecals++;

      if (_pageInfoLabel != null)
        _pageInfoLabel.text = $" {totalDecals - 2} - {_currentPage + 1}/{totalPages} ";

      RefreshSelectionVisuals();

      _upFolderElement.SetEnabled(_currentGroupNode?.Parent != null);
      _topButton.SetEnabled(_currentPage > 0);
      _prevButton.SetEnabled(_currentPage > 0);
      _nextButton.SetEnabled(_currentPage < totalPages - 1);
    }

    private static bool ContainsDescendant(GroupNode parent, GroupNode target)
    {
      foreach (var child in parent.Children)
      {
        if (child == target || ContainsDescendant(child, target))
          return true;
      }
      return false;
    }

    private int ComputeTotalPages()
    {
      int totalItems = _decalElements.Count - 1;
      return Mathf.Max(1, Mathf.CeilToInt((float)totalItems / NormalSlots));
    }

    internal void PlayClickSound() => _uiSoundController.PlayClickSound();

    private VisualElement CreateFolderElement(string labelText, object userData, Action onClick, GroupNode node = null)
    {
      var root = new VisualElement();
      root.style.width = CellSize;
      root.style.height = CellSize;
      root.style.marginLeft = 3;
      root.style.marginRight = 3;
      root.style.marginTop = 3;
      root.style.marginBottom = 3;
      root.style.paddingTop = 12;
      root.style.flexDirection = FlexDirection.Column;
      root.style.alignItems = Align.Center;
      root.style.justifyContent = Justify.FlexStart;
      root.style.backgroundColor = new Color(21 / 255f, 39 / 255f, 34 / 255f, 1f);
      root.style.borderTopColor = root.style.borderBottomColor = root.style.borderLeftColor = root.style.borderRightColor = new StyleColor(new Color(54 / 255f, 48 / 255f, 35 / 255f, 1f));
      root.style.borderTopWidth = root.style.borderBottomWidth = root.style.borderLeftWidth = root.style.borderRightWidth = SlotBorderWidth;
      root.userData = userData;

      string formattedText;
      if (labelText == "..")
      {
        formattedText = "\n ..";
      }
      else if (node != null && !node.IsSpecGroup)
      {
        formattedText = _loc.T(FolderTagKey) + "\n" + labelText;
      }
      else if (node != null && (node.Parent == null || string.IsNullOrEmpty(node.Parent.Id)))
      {
        formattedText = _loc.T(ModTagKey) + "\n" + labelText;
      }
      else
      {
        formattedText = _loc.T(GroupTagKey) + "\n" + labelText;
      }

      var label = new Label(formattedText);
      label.style.unityTextAlign = TextAnchor.UpperCenter;
      label.style.whiteSpace = WhiteSpace.Normal;
      label.style.fontSize = FontSize1;
      label.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.9f, 1f));
      root.Add(label);

      root.RegisterCallback<ClickEvent>(evt =>
      {
        PlayClickSound();
        onClick();
      });

      root.RegisterCallback<MouseEnterEvent>(evt =>
      {
        if (_bottomTitle != null)
          _bottomTitle.text = userData?.ToString() ?? "";
      });

      root.RegisterCallback<MouseLeaveEvent>(evt =>
      {
        if (_bottomTitle != null)
          _bottomTitle.text = "";
      });

      return root;
    }
    private VisualElement CreateDecalElement(Decal decal)
    {
      var root = new VisualElement();
      root.style.width = CellSize;
      root.style.height = CellSize;
      root.style.marginLeft = 3;
      root.style.marginRight = 3;
      root.style.marginTop = 3;
      root.style.marginBottom = 3;
      root.style.position = Position.Relative;
      root.style.alignItems = Align.Center;
      root.style.justifyContent = Justify.Center;

      var texture = _decalService.GetDecalTexture(decal);
      if (texture != null)
        root.style.backgroundImage = new StyleBackground(texture);

      root.style.backgroundColor = new Color(98 / 255f, 83 / 255f, 66 / 255f, 1f);
      root.style.unityBackgroundImageTintColor = new StyleColor(SelectedTint);
      root.userData = decal;

      var frame = new VisualElement();
      frame.style.position = Position.Absolute;
      frame.style.width = Length.Percent(100);
      frame.style.height = Length.Percent(100);
      frame.style.borderTopWidth = frame.style.borderBottomWidth = frame.style.borderLeftWidth = frame.style.borderRightWidth = SlotBorderWidth;
      frame.style.borderTopColor = frame.style.borderBottomColor = frame.style.borderLeftColor = frame.style.borderRightColor = new StyleColor(FrameNormal);
      frame.pickingMode = PickingMode.Ignore;
      root.Add(frame);

      root.RegisterCallback<ClickEvent>(evt =>
      {
        if (_supplier == null)
          return;
        PlayClickSound();
        _supplier.SetActiveDecal(decal);
        RefreshSelectionVisuals();
        if (evt.clickCount >= 2)
          Close();
      });

      root.RegisterCallback<MouseEnterEvent>(evt =>
      {
        if (_bottomTitle != null && root.userData is Decal d)
          _bottomTitle.text = d.Id;
      });

      root.RegisterCallback<MouseLeaveEvent>(evt =>
      {
        if (_bottomTitle != null)
          _bottomTitle.text = "";
      });

      return root;
    }
    private static void AddSlotLabel(VisualElement slot, string text)
    {
      var label = new Label(text);
      label.style.position = Position.Absolute;
      label.style.bottom = 0;
      label.style.marginBottom = 6;
      label.style.marginLeft = StyleKeyword.Auto;
      label.style.marginRight = StyleKeyword.Auto;
      label.style.unityTextAlign = TextAnchor.LowerCenter;
      label.style.fontSize = 12;
      label.style.color = Color.white;
      label.style.backgroundColor = new Color(0, 0, 0, 0.5f);
      label.style.paddingLeft = 3;
      label.style.paddingRight = 3;
      label.style.paddingBottom = 3;
      slot.Add(label);
    }

    private void UpdateCurrentDecalSlot()
    {
      if (_currentDecalElement == null || _supplier == null) return;
      var activeDecal = _supplier.ActiveDecal;
      var texture = _decalService.GetDecalTexture(activeDecal);
      if (texture != null)
        _currentDecalElement.style.backgroundImage = new StyleBackground(texture);
      _currentDecalElement.userData = activeDecal;
    }

    private void RefreshSelectionVisuals()
    {
      var activeDecal = _supplier?.ActiveDecal ?? default;

      foreach (var element in _decalElements)
      {
        if (element.userData is Decal decal)
        {
          bool isActive = !activeDecal.IsEmpty && decal.Equals(activeDecal);
          var borderColor = new StyleColor(isActive ? FrameSelected : FrameNormal);

          var frame = element.ElementAt(0);
          if (frame != null)
          {
            frame.style.borderTopColor = frame.style.borderBottomColor = frame.style.borderLeftColor = frame.style.borderRightColor = borderColor;
          }
        }
      }

      // Gold borders for Original and Current slots
      var gold = new StyleColor(FrameSelected);
      if (_originalDecalElement != null)
      {
        var frame = _originalDecalElement.ElementAt(0);
        if (frame != null)
          frame.style.borderTopColor = frame.style.borderBottomColor = frame.style.borderLeftColor = frame.style.borderRightColor = gold;
      }
      if (_currentDecalElement != null)
      {
        var frame = _currentDecalElement.ElementAt(0);
        if (frame != null)
          frame.style.borderTopColor = frame.style.borderBottomColor = frame.style.borderLeftColor = frame.style.borderRightColor = gold;
      }
    }

    internal void Close()
    {
      if (!IsShowing)
        return;
      if (_supplier != null)
      {
        if (_activeDecalHandler != null)
          _supplier.ActiveDecalChanged -= _activeDecalHandler;
        SaveGroupState();
      }
      _eventBus.Unregister(this);
      _panelStack.Pop(this);
    }

    private void SaveGroupState()
    {
      if (_supplier != null)
        _lastGroupIdByCategory[_supplier.Category] = _currentGroupNode?.Id ?? "";
    }

    private Button CreateBarControl(string text, int width, TextAnchor alignment, Action onClick = null)
    {
      var btn = new Button();
      btn.text = text;
      btn.AddToClassList("game-text-heading");
      btn.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.9f, 1f));

      btn.style.fontSize = FontSize1;
      btn.style.width = width;
      btn.style.height = 28;
      btn.style.backgroundColor = new Color(21 / 255f, 39 / 255f, 34 / 255f, 1f);
      btn.style.borderTopColor = btn.style.borderBottomColor = btn.style.borderLeftColor = btn.style.borderRightColor = new StyleColor(new Color(166 / 255f, 143 / 255f, 97 / 255f, 1f));
      btn.style.borderTopWidth = btn.style.borderBottomWidth = btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
      btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius = 2;
      btn.style.marginLeft = 3;
      btn.style.marginRight = 3;
      btn.style.marginTop = 0;
      btn.style.marginBottom = 0;
      btn.style.unityTextAlign = alignment;

      if (onClick != null)
        btn.RegisterCallback<ClickEvent>(evt => { PlayClickSound(); onClick(); });

      return btn;
    }

    bool IPanelController.OnUIConfirmed() => false;

    void IPanelController.OnUICancelled() => Close();
  }

  public static class UIDebugExtensions
  {
    public static void AddDebugBordersRecursive(this VisualElement container, Color color, float borderWidth = 1f)
    {
      if (container == null)
      {
        return;
      }

      List<VisualElement> targetElements = new List<VisualElement>();
      GatherElementsElements(container, targetElements);

      foreach (VisualElement element in targetElements)
      {
        ApplyDebugBorder(element, color, borderWidth);
      }
    }

    private static void GatherElementsElements(VisualElement current, List<VisualElement> list)
    {
      if (current == null)
      {
        return;
      }

      // Skip processing existing debug borders so they aren't gathered into the tree
      if (current.userData is string tag && tag == "TimberbornDebugBorder")
      {
        return;
      }

      list.Add(current);

      int count = current.childCount;
      for (int i = 0; i < count; i++)
      {
        GatherElementsElements(current[i], list);
      }
    }

    private static void ApplyDebugBorder(VisualElement container, Color color, float borderWidth)
    {
      // Clean up any existing debug borders on this container first to prevent accumulation
      for (int i = container.childCount - 1; i >= 0; i--)
      {
        VisualElement child = container[i];
        if (child != null && child.userData is string tag && tag == "TimberbornDebugBorder")
        {
          child.RemoveFromHierarchy();
        }
      }

      VisualElement debugBorder = new VisualElement();

      // Tag this element so it can be identified and cleared on subsequent repopulations
      debugBorder.userData = "TimberbornDebugBorder";

      debugBorder.style.position = Position.Absolute;

      debugBorder.style.left = 0f;
      debugBorder.style.right = 0f;
      debugBorder.style.top = 0f;
      debugBorder.style.bottom = 0f;

      debugBorder.style.borderTopWidth = borderWidth;
      debugBorder.style.borderBottomWidth = borderWidth;
      debugBorder.style.borderLeftWidth = borderWidth;
      debugBorder.style.borderRightWidth = borderWidth;

      debugBorder.style.borderTopColor = color;
      debugBorder.style.borderBottomColor = color;
      debugBorder.style.borderLeftColor = color;
      debugBorder.style.borderRightColor = color;

      debugBorder.style.backgroundColor = Color.clear;

      debugBorder.pickingMode = PickingMode.Ignore;

      container.Add(debugBorder);
      debugBorder.BringToFront();
    }
  }
}
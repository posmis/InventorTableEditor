using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Inventor;
using InventorTableEditor.Addin.Drawing;
using InventorTableEditor.Addin.UI;

namespace InventorTableEditor.Addin;

/// <summary>
/// Точка входа аддина. GUID должен совпадать с ClassId в InventorTableEditor.addin.
/// </summary>
[ComVisible(true)]
[Guid(AddinGuid)]
public class StandardAddInServer : ApplicationAddInServer
{
    public const string AddinGuid = "01FC19AB-85AE-46CD-A9F7-842467069C51";
    public const string ClientId = "{" + AddinGuid + "}";

    private const string PanelId = "InventorTableEditor.Panel";

    /// <summary>Вкладки, на которые панель уже добавлена (по внутреннему имени).</summary>
    private readonly HashSet<string> _tabsWithPanel = new();

    private readonly TableScaleGuard _scaleGuard = new();

    private Inventor.Application? _inventor;
    private TableCommands? _commands;
    private ButtonDefinition? _newTableButton;
    private ButtonDefinition? _editTableButton;
    private UserInputEvents? _userInputEvents;
    private ApplicationEvents? _applicationEvents;
    private UserInterfaceEvents? _userInterfaceEvents;

    public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        try
        {
            AddinLog.Write($"Загрузка аддина, firstTime={firstTime}");
            _inventor = addInSiteObject.Application;
            _commands = new TableCommands(_inventor);

            var definitions = _inventor.CommandManager.ControlDefinitions;
            _newTableButton = definitions.AddButtonDefinition(
                "Новая\nтаблица", "InventorTableEditor.NewTable", CommandTypesEnum.kShapeEditCmdType, ClientId,
                "Создать таблицу и вставить в чертёж", "Открывает редактор новой таблицы",
                Icon(16, pencil: false), Icon(32, pencil: false),
                ButtonDisplayEnum.kAlwaysDisplayText);
            _newTableButton.OnExecute += OnNewTable;

            _editTableButton = definitions.AddButtonDefinition(
                "Редактировать\nтаблицу", "InventorTableEditor.EditTable", CommandTypesEnum.kShapeEditCmdType, ClientId,
                "Редактировать выбранную таблицу", "Открывает выбранную таблицу в редакторе. Можно также дважды щёлкнуть по таблице",
                Icon(16, pencil: true), Icon(32, pencil: true),
                ButtonDisplayEnum.kAlwaysDisplayText);
            _editTableButton.OnExecute += OnEditTable;

            _userInputEvents = _inventor.CommandManager.UserInputEvents;
            _userInputEvents.OnDoubleClick += _commands.OnDoubleClick;

            // Вкладки других надстроек (например «Пояснения ЕСКД») могут появиться позже нашего аддина,
            // поэтому панель добавляется повторно при открытии документов и перестроении ленты.
            _applicationEvents = _inventor.ApplicationEvents;
            _applicationEvents.OnActivateDocument += OnActivateDocument;
            _userInterfaceEvents = _inventor.UserInterfaceManager.UserInterfaceEvents;
            _userInterfaceEvents.OnResetRibbonInterface += OnResetRibbonInterface;

            AddRibbonPanels();

            if (_inventor.ActiveDocument is DrawingDocument activeDrawing)
                _scaleGuard.Attach(activeDrawing);
        }
        catch (Exception ex)
        {
            AddinLog.Write("Ошибка загрузки: " + ex);
            System.Windows.MessageBox.Show("Ошибка загрузки аддина «Редактор таблиц»:\n\n" + ex, "Редактор таблиц",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private static object Icon(int size, bool pencil)
    {
        try
        {
            return IconFactory.CreateTableIcon(size, pencil);
        }
        catch (Exception ex)
        {
            AddinLog.Write("Не удалось создать иконку: " + ex.Message);
            return Type.Missing;
        }
    }

    private void OnActivateDocument(_Document documentObject, EventTimingEnum beforeOrAfter, NameValueMap context, out HandlingCodeEnum handlingCode)
    {
        handlingCode = HandlingCodeEnum.kEventNotHandled;
        if (beforeOrAfter != EventTimingEnum.kAfter)
            return;

        AddRibbonPanels();
        if (documentObject is DrawingDocument drawing)
        {
            try
            {
                _scaleGuard.Attach(drawing);
            }
            catch (Exception ex)
            {
                AddinLog.Write("Не удалось подписаться на изменения чертежа: " + ex.Message);
            }
        }
    }

    private void OnResetRibbonInterface(NameValueMap context)
    {
        _tabsWithPanel.Clear();
        AddRibbonPanels();
    }

    /// <summary>
    /// Добавляет панель «Таблицы» в конец (справа) каждой вкладки пояснений чертежа:
    /// стандартной «Пояснения» и вкладок других надстроек (например «Пояснения ЕСКД»).
    /// </summary>
    private void AddRibbonPanels()
    {
        if (_inventor == null)
            return;

        try
        {
            var log = new System.Text.StringBuilder("Вкладки чертежа:");
            foreach (RibbonTab tab in _inventor.UserInterfaceManager.Ribbons["Drawing"].RibbonTabs)
            {
                var internalName = tab.InternalName;
                var target = IsAnnotationTab(tab);
                log.Append($"\n    {internalName} | {tab.DisplayName}{(target ? "  <- панель" : string.Empty)}");

                if (!target || _tabsWithPanel.Contains(internalName))
                    continue;

                try
                {
                    AddPanel(tab);
                    _tabsWithPanel.Add(internalName);
                }
                catch (Exception ex)
                {
                    log.Append($"  ОШИБКА: {ex.Message}");
                }
            }

            AddinLog.Write(log.ToString());
        }
        catch (Exception ex)
        {
            AddinLog.Write("Ошибка добавления панели: " + ex);
        }
    }

    private static bool IsAnnotationTab(RibbonTab tab) =>
        tab.InternalName == "id_TabAnnotate"
        || tab.DisplayName.Contains("Пояснен", StringComparison.OrdinalIgnoreCase)
        || tab.DisplayName.Contains("ЕСКД", StringComparison.OrdinalIgnoreCase)
        || tab.DisplayName.Contains("Annotat", StringComparison.OrdinalIgnoreCase);

    private void AddPanel(RibbonTab tab)
    {
        // Внутреннее имя панели должно быть уникальным в пределах ленты.
        var panelId = PanelId + "." + tab.InternalName;
        RibbonPanel? panel = null;
        foreach (RibbonPanel existing in tab.RibbonPanels)
        {
            if (existing.InternalName == panelId)
                panel = existing;
        }

        // Без целевой панели новая панель добавляется в конец вкладки, то есть справа.
        panel ??= tab.RibbonPanels.Add("Таблицы", panelId, ClientId, string.Empty, false);

        if (panel.CommandControls.Count == 0)
        {
            panel.CommandControls.AddButton(_newTableButton, true);
            panel.CommandControls.AddButton(_editTableButton, true);
        }
    }

    private void OnNewTable(NameValueMap context) => _commands?.NewTable();

    private void OnEditTable(NameValueMap context) => _commands?.EditSelected();

    public void Deactivate()
    {
        if (_userInputEvents != null && _commands != null)
            _userInputEvents.OnDoubleClick -= _commands.OnDoubleClick;

        if (_applicationEvents != null)
            _applicationEvents.OnActivateDocument -= OnActivateDocument;

        _scaleGuard.DetachAll();

        if (_userInterfaceEvents != null)
            _userInterfaceEvents.OnResetRibbonInterface -= OnResetRibbonInterface;

        if (_newTableButton != null)
        {
            _newTableButton.OnExecute -= OnNewTable;
            _newTableButton.Delete();
        }

        if (_editTableButton != null)
        {
            _editTableButton.OnExecute -= OnEditTable;
            _editTableButton.Delete();
        }

        _userInputEvents = null;
        _applicationEvents = null;
        _userInterfaceEvents = null;
        _newTableButton = null;
        _editTableButton = null;
        _commands = null;
        _tabsWithPanel.Clear();

        if (_inventor != null)
        {
            Marshal.ReleaseComObject(_inventor);
            _inventor = null;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void ExecuteCommand(int commandID)
    {
        // Устарело в API Inventor, команды идут через ControlDefinitions.
    }

    public object? Automation => null;
}

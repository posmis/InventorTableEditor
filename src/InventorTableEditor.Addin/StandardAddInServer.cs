using System;
using System.Runtime.InteropServices;
using Inventor;
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

    private Inventor.Application? _inventor;
    private TableCommands? _commands;
    private ButtonDefinition? _newTableButton;
    private ButtonDefinition? _editTableButton;
    private UserInputEvents? _userInputEvents;

    public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        _inventor = addInSiteObject.Application;
        _commands = new TableCommands(_inventor);

        var definitions = _inventor.CommandManager.ControlDefinitions;
        _newTableButton = definitions.AddButtonDefinition(
            "Новая\nтаблица", "InventorTableEditor.NewTable", CommandTypesEnum.kShapeEditCmdType, ClientId,
            "Создать таблицу и вставить в чертёж", "Открывает редактор новой таблицы",
            IconFactory.CreateTableIcon(16, pencil: false), IconFactory.CreateTableIcon(32, pencil: false),
            ButtonDisplayEnum.kAlwaysDisplayText);
        _newTableButton.OnExecute += OnNewTable;

        _editTableButton = definitions.AddButtonDefinition(
            "Редактировать\nтаблицу", "InventorTableEditor.EditTable", CommandTypesEnum.kShapeEditCmdType, ClientId,
            "Редактировать выбранную таблицу", "Открывает выбранную таблицу в редакторе. Можно также дважды щёлкнуть по таблице",
            IconFactory.CreateTableIcon(16, pencil: true), IconFactory.CreateTableIcon(32, pencil: true),
            ButtonDisplayEnum.kAlwaysDisplayText);
        _editTableButton.OnExecute += OnEditTable;

        AddRibbonPanel();

        _userInputEvents = _inventor.CommandManager.UserInputEvents;
        _userInputEvents.OnDoubleClick += _commands.OnDoubleClick;
    }

    /// <summary>
    /// Добавляет панель «Таблицы» в конец (справа) каждой вкладки пояснений чертежа:
    /// стандартной «Пояснения» и вкладок, добавленных другими надстройками (например «Пояснения ЕСКД»).
    /// </summary>
    private void AddRibbonPanel()
    {
        var added = false;
        foreach (RibbonTab tab in _inventor!.UserInterfaceManager.Ribbons["Drawing"].RibbonTabs)
        {
            if (!IsAnnotationTab(tab))
                continue;

            try
            {
                AddPanel(tab);
                added = true;
            }
            catch (COMException)
            {
                // Вкладка не принимает панели — пропускаем, остальные вкладки важнее.
            }
        }

        if (!added)
            AddPanel(_inventor.UserInterfaceManager.Ribbons["Drawing"].RibbonTabs["id_TabAnnotate"]);
    }

    private static bool IsAnnotationTab(RibbonTab tab) =>
        tab.InternalName == "id_TabAnnotate"
        || tab.DisplayName.Contains("Пояснен", StringComparison.OrdinalIgnoreCase)
        || tab.DisplayName.Contains("Annotat", StringComparison.OrdinalIgnoreCase);

    private void AddPanel(RibbonTab tab)
    {
        // Внутреннее имя панели должно быть уникальным в пределах ленты.
        var panelId = PanelId + "." + tab.InternalName;
        RibbonPanel panel;
        try
        {
            panel = tab.RibbonPanels[panelId];
        }
        catch (COMException)
        {
            // Без целевой панели новая панель добавляется в конец вкладки, то есть справа.
            panel = tab.RibbonPanels.Add("Таблицы", panelId, ClientId, string.Empty, false);
        }

        panel.CommandControls.AddButton(_newTableButton, true);
        panel.CommandControls.AddButton(_editTableButton, true);
    }

    private void OnNewTable(NameValueMap context) => _commands?.NewTable();

    private void OnEditTable(NameValueMap context) => _commands?.EditSelected();

    public void Deactivate()
    {
        if (_userInputEvents != null && _commands != null)
            _userInputEvents.OnDoubleClick -= _commands.OnDoubleClick;

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
        _newTableButton = null;
        _editTableButton = null;
        _commands = null;

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

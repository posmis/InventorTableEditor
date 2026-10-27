using System;
using System.Runtime.InteropServices;
using Inventor;

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

    private Inventor.Application? _inventor;

    public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        _inventor = addInSiteObject.Application;
        // TODO: кнопки на вкладке «Пояснения», обработчик двойного клика.
    }

    public void Deactivate()
    {
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

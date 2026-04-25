namespace Celeste.Mod.MiaoNet;

public delegate void SettingsChangedEventHandler<TSettings>(MiaoNetModuleSettings settings, SettingsCategory category);

public interface INotifySettingsChanged<TSettings>
{
    public event SettingsChangedEventHandler<TSettings> SettingsChanged;
}

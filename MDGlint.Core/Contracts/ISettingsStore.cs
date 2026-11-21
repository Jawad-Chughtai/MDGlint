using MDGlint.Core.Models;

namespace MDGlint.Core.Contracts;

public interface ISettingsStore
{
    AppSettings Load();

    /// <summary>
    /// Saves the settings; never throws. If the file can't be written, callers keep the change in
    /// memory, so it lasts for this session.
    /// </summary>
    void Save(AppSettings settings);
}

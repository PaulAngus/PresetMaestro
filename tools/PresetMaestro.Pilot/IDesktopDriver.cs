namespace PresetMaestro.Pilot;

internal interface IDesktopDriver : IDisposable
{
    void Invoke(string id);
    string Text(string id);
    bool Enabled(string id);
    void Type(string keys);
    void Resize(int width, int height);
    void Capture(string path);
    void Close();
}

namespace AriadGSM.Vision.Windows;

public interface IWindowEnumerator
{
    IReadOnlyList<WindowSnapshot> GetVisibleWindows();
}


namespace Project;

// Giao diện (Views, wwwroot) nằm ở thư mục frontend/ cạnh backend/.
public static class FrontendPaths
{
    public static string WebRoot(string backendRoot) =>
        Path.GetFullPath(Path.Combine(backendRoot, "..", "frontend", "wwwroot"));
}

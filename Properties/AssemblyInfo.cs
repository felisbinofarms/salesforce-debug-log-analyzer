using System.Reflection;

// GenerateAssemblyInfo=false is set in the .csproj to avoid duplicate attribute errors
// caused by the WPF _wpftmp.csproj sharing the same output directory.
// These attributes must be declared manually so that the managed assembly version
// matches the MSBuild AssemblyVersion property (1.0.0.0) that the BAML/XAML compiler
// stamps into pack:// resource URIs.  Without them the assembly loads as 0.0.0.0 and
// WPF throws FileNotFoundException when resolving App.xaml resources at startup.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

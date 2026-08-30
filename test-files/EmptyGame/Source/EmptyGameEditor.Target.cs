using UnrealBuildTool;

public class EmptyGameEditorTarget : TargetRules {
    public EmptyGameEditorTarget(TargetInfo Target) : base(Target) {
        Type = TargetType.Editor;
        DefaultBuildSettings = BuildSettingsVersion.Latest;
        ExtraModuleNames.Add("EmptyGame");
    }
}

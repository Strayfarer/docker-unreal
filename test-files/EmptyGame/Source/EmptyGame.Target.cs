using UnrealBuildTool;

public class EmptyGameTarget : TargetRules {
    public EmptyGameTarget(TargetInfo Target) : base(Target) {
        Type = TargetType.Game;
        DefaultBuildSettings = BuildSettingsVersion.Latest;
        ExtraModuleNames.Add("EmptyGame");
    }
}

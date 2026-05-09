using Rage;

public class AudioEmitterInteract : InteriorInteract
{
    public AudioEmitterInteract()
    {
    }

    public AudioEmitterInteract(string name, Vector3 position, float heading, string buttonPromptText)
        : base(name, position, heading, buttonPromptText)
    {
    }

    public override void AddPrompt()
    {
        if (Player != null)
        {
            Player.ButtonPrompts.AttemptAddPrompt(Name, ButtonPromptText, Name, Settings.SettingsManager.KeySettings.InteractStart, 999);
        }
    }
}

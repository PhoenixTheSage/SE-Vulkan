namespace ClientPlugin.Bootstrap;

// Shared by Display UI and executable tests; persistence only occurs at an accepted close.
public sealed class DisplaySelectionTransaction
{
    public RendererBackend Draft { get; set; }
    public bool OkInProgress { get; private set; }
    public bool AwaitingResolutionConfirmation { get; private set; }
    public bool ResolutionAccepted { get; private set; }
    public DisplaySelectionTransaction(RendererBackend initial) => Draft = initial;
    public void BeginOk() { OkInProgress = true; ResolutionAccepted = false; }
    public void BeginResolutionConfirmation() => AwaitingResolutionConfirmation = true;
    public void ConfirmResolution(bool accepted)
    {
        AwaitingResolutionConfirmation = false; ResolutionAccepted = accepted;
        if (!accepted) OkInProgress = false;
    }
    public bool AcceptClose(bool immediate) => immediate ? ResolutionAccepted : OkInProgress && !AwaitingResolutionConfirmation;
    public void EndOk() => OkInProgress = false;
    public void Cancel() { OkInProgress = false; AwaitingResolutionConfirmation = false; ResolutionAccepted = false; }
}

namespace XrmPocket.Api.Connections;

/// <summary>Body bij het starten van een interactieve aanmelding.</summary>
public sealed class SignInRequest
{
    /// <summary>
    /// Origin van de UI (bijv. http://192.168.9.9:4108). Moet in de allow-list staan,
    /// want de callback-URL moet exact overeenkomen met de app registration.
    /// </summary>
    public string? Origin { get; set; }
}

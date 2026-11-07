using CommunityToolkit.Mvvm.ComponentModel;
using Limitless.Studio.Services;

namespace Limitless.Studio.ViewModels;

/// <summary>Base des pages de la nav latérale — titre/contexte pour
/// l'en-tête + accès au contexte partagé (chemins, services,
/// statut). `Activate()` est appelé par le shell à la première
/// sélection (chargement paresseux des données).</summary>
public abstract partial class PageViewModel : ViewModelBase
{
    protected PageViewModel(StudioContext ctx) => Ctx = ctx;

    protected StudioContext Ctx { get; }

    /// <summary>Titre de la page (en-tête).</summary>
    public abstract string Title { get; }

    /// <summary>Contexte — dataset/source sous le titre.</summary>
    public abstract string Subtitle { get; }

    /// <summary>True tant que la page n'a pas encore été activée.</summary>
    [ObservableProperty] private bool _activated;

    /// <summary>Message d'état vide/erreur affiché quand la liste
    /// principale est vide (copy utilitaire EN).</summary>
    [ObservableProperty] private string _emptyMessage = "";

    /// <summary>Appelé quand la page devient visible — charge les
    /// données au premier affichage.</summary>
    public void Activate()
    {
        if (Activated) return;
        Activated = true;
        OnFirstActivate();
    }

    protected virtual void OnFirstActivate() { }

    protected void Status(string s) => Ctx.Status(s);
}

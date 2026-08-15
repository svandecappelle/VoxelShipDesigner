# ShipDesign

Application desktop (.NET 7 / WPF) générant des vaisseaux spatiaux **entièrement de manière
procédurale** — coque, ailes, moteurs, cockpit et greebles sont construits par code à partir
d'une quinzaine de paramètres continus (longueur, largeur, effilement, angle de flèche,
nombre de tuyères, couleurs de livrée...) — et les exportant en `.glb` pour Unity.

L'UI reprend l'identité visuelle du mockup `vessel-forge.html` (thème HUD sombre cyan/ambre) ;
l'algorithme de génération (profil de coque en révolution, ailes trapézoïdales, tuyères,
cockpit, greebles dispersés par seed) est un port en C#/SharpGLTF de celui du mockup.

## Structure

- `src/ShipDesign.Core` — modèles et génération procédurale. Ne dépend pas de WPF (réutilisable
  telle quelle : tests, CLI, etc.).
  - `Procedural/ShipParameters.cs` — tous les paramètres du générateur.
  - `Procedural/HullBuilder.cs` — coque : surface de révolution le long de l'axe Z, profil de
    rayon param étrique (effilement du nez, bulbe médian, rétrécissement de la poupe), normales
    lisses calculées analytiquement.
  - `Procedural/WingBuilder.cs` — paire d'ailes trapézoïdales extrudées ; l'aile n'est modélisée
    qu'une fois puis instanciée mirorée (échelle -1 en X) pour l'autre côté.
  - `Procedural/EngineBuilder.cs` — tuyères (cylindre effilé ou anneau) + disque émissif,
    positionnées en cercle à l'arrière selon leur nombre.
  - `Procedural/CockpitBuilder.cs` — bulle (demi-sphère partielle) ou verrière plate, matériau
    transparent teinté.
  - `Procedural/GreebleBuilder.cs` — détails de surface dispersés (seedés) + anneaux de pont.
  - `Procedural/MeshUtil.cs` — utilitaires bas niveau partagés (anneaux de sommets, tore,
    liaison en bandes de quads) ; toute la géométrie a été validée triangle par triangle
    (cohérence normale/winding) via des tests jetables pendant le développement.
  - `Procedural/ProceduralShipBuilder.cs` — point d'entrée : assemble tout en un seul
    `SharpGLTF.Schema2.ModelRoot`, plus les stats d'affichage (désignation, classe de masse).
- `src/ShipDesign.App` — interface WPF (viewport HelixToolkit).
  - Thème sombre façon HUD sci-fi ([Theme.xaml](src/ShipDesign.App/Theme.xaml) : palette
    cyan/ambre, panneaux, sélecteurs en boutons segmentés, slider à curseur ambre, overlay de
    stats et réticule animé dans le viewport, toggle d'orbite automatique de la caméra).
  - Tous les paramètres sont modifiables en direct (chaque changement régénère le vaisseau) :
    classe de coque, géométrie, ailes, propulsion, cockpit, détails de surface (seed inclus),
    livrée (4 couleurs, palette de préréglages). Bouton "Vaisseau aléatoire" et export `.glb`.

## Lancer l'application

```
dotnet run --project src/ShipDesign.App
```

## Générer un vaisseau (API Core)

```csharp
var parameters = new ShipParameters
{
    HullClass = HullClass.Fighter,
    Length = 18f,
    Beam = 4f,
    WingStyle = WingStyle.Swept,
    EngineCount = 2,
    Seed = 1234,
};

var model = ProceduralShipBuilder.Build(parameters);
model.SaveGLB("output/fighter.glb");
```

Le `.glb` généré s'importe directement dans Unity (glisser-déposer dans `Assets/`).

## Étendre le générateur

- **Nouvelle classe de coque** : ajouter une entrée à `HullClassPreset.All` (fraction du nez,
  bulbe, segments radiaux, ratio de poupe, préfixe de désignation) et à l'enum `HullClass`.
- **Nouveau style d'aile/moteur/cockpit** : ajouter une valeur à l'enum correspondant
  (`WingStyle`, `EngineStyle`, `CockpitStyle`) et le cas dans le builder associé ; l'UI
  (boutons segmentés) se met à jour automatiquement puisqu'elle itère sur `Enum.GetValues<T>()`.
  Penser à ajouter le libellé français dans
  [EnumLabelConverter](src/ShipDesign.App/Converters/EnumLabelConverter.cs).

## Prochaines étapes

- Undo / historique (actuellement, chaque changement régénère directement, pas d'annulation).
- Sélecteur de couleur libre (actuellement une palette de 8 préréglages par canal) plutôt
  qu'un simple ensemble de pastilles.
- Fenêtre sans chrome natif pour coller davantage au mockup (barre de titre Windows actuelle).

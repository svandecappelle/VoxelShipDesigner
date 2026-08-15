# ShipDesign

Application desktop (.NET 7 / WPF) générant des vaisseaux spatiaux **entièrement de manière
procédurale** — coque principale, nacelles secondaires sur pylônes, ailes, moteurs, cockpit,
tourelle de commandement, tourelles d'armement et greebles sont construits par code à partir
d'une vingtaine de paramètres continus (longueur, largeur, effilement, angle de flèche, nombre
de tuyères/tourelles, couleurs de livrée...) — et les exportant en `.glb` pour Unity.

L'UI reprend l'identité visuelle du mockup `vessel-forge.html` (thème HUD sombre cyan/ambre).
La coque utilise volontairement un langage visuel **anguleux/hard-surface** (facettes nettes,
sections en octogone chanfreiné, silhouettes façon Star Destroyer/X-wing) plutôt qu'une
révolution lisse façon avion de chasse — voir "Pourquoi une coque anguleuse ?" plus bas.

## Structure

- `src/ShipDesign.Core` — modèles et génération procédurale. Ne dépend pas de WPF (réutilisable
  telle quelle : tests, CLI, etc.).
  - `Procedural/ShipParameters.cs` — tous les paramètres du générateur.
  - `Procedural/HullBuilder.cs` — coque : séquence de sections en octogone chanfreiné (rectangle
    aux coins coupés) le long de l'axe Z, largeur/hauteur interpolées linéairement entre les
    points de contrôle du profil (voir `HullClassPreset`), normales plates par face (pas de
    lissage — c'est voulu, pour un rendu "panneaux" plutôt qu'aérodynamique). `BuildVolume` est
    la forme générique (profil/longueur/largeur en paramètres) réutilisée par `NacelleBuilder`.
  - `Procedural/NacelleBuilder.cs` — paire de nacelles (mini-coques tubulaires, même algorithme
    que la coque principale mais un profil différent) montées sur pylônes de part et d'autre de
    la coque, parallèles à son axe — la coque cesse d'être un volume unique.
  - `Procedural/WingBuilder.cs` — paire d'ailes trapézoïdales extrudées ; l'aile n'est modélisée
    qu'une fois puis instanciée mirorée (échelle -1 en X) pour l'autre côté.
  - `Procedural/EngineBuilder.cs` — tuyères (cylindre effilé ou anneau) + disque émissif,
    positionnées en cercle à l'arrière selon leur nombre.
  - `Procedural/CockpitBuilder.cs` — bulle (demi-sphère partielle) ou verrière plate, matériau
    transparent teinté.
  - `Procedural/SuperstructureBuilder.cs` — tourelle de commandement (pont) en 2 étages,
    posée sur le dessus de la coque, en arrière du centre — l'élément qui casse le plus une
    silhouette "fuselage unique" en lecture "vaisseau capital" (absent pour les chasseurs,
    trop petits pour avoir un pont séparé).
  - `Procedural/GreebleBuilder.cs` — détails de surface dispersés (seedés), tourelles
    d'armement (positions déterministes, alternées de part et d'autre de la ligne dorsale) et
    colliers structurels massifs (pas de simples lignes fines) aux jonctions de pont.
  - `Procedural/MeshUtil.cs` — utilitaires bas niveau partagés (anneaux de sommets, tore,
    boîtes, liaison en bandes de quads) ; toute la géométrie a été validée triangle par
    triangle (cohérence normale/winding) via des tests jetables pendant le développement.
  - `Procedural/ProceduralShipBuilder.cs` — point d'entrée : assemble tout en un seul
    `SharpGLTF.Schema2.ModelRoot`, plus les stats d'affichage (désignation, classe de masse).
- `src/ShipDesign.App` — interface WPF (viewport HelixToolkit).
  - Thème sombre façon HUD sci-fi ([Theme.xaml](src/ShipDesign.App/Theme.xaml) : palette
    cyan/ambre, panneaux, sélecteurs en boutons segmentés, slider à curseur ambre, overlay de
    stats et réticule animé dans le viewport, toggle d'orbite automatique de la caméra).
  - Tous les paramètres sont modifiables en direct (chaque changement régénère le vaisseau) :
    classe de coque, géométrie, ailes, propulsion, cockpit, structures secondaires (tourelle de
    commandement, nacelles), détails de surface (greebles, tourelles, seed inclus), livrée
    (4 couleurs, palette de préréglages). Bouton "Vaisseau aléatoire" et export `.glb`.

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

## Pourquoi une coque anguleuse ?

Une coque en révolution lisse (surface générée en tournant un profil continu autour de l'axe
Z, normales lissées) donne visuellement un fuselage d'avion — un langage aérodynamique qui n'a
pas de raison d'être dans le vide spatial, et qui ne correspond pas à l'esthétique de la SF
"space opera" (Star Wars, Star Trek, Battlestar Galactica...). `HullBuilder` construit donc la
coque comme une suite de sections en octogone chanfreiné dont la largeur/hauteur varient
**linéairement** entre les points de contrôle de `HullClassPreset` (pas de courbe lissée : la
pente change brutalement à chaque point, ce qui crée des arêtes visibles), avec des normales
plates par face plutôt que lissées. Chaque classe de coque a un profil très différent pour
rappeler un archétype reconnaissable :

- **Chasseur** — nez en aiguille, proportions fines, chanfrein modéré.
- **Corvette** — nez en aiguille, corps central plus long et plus plat.
- **Cargo** — nez quasi-tronqué, corps très cubique (chanfrein minimal), long palier plat.
- **Croiseur** — coque en coin plate façon Star Destroyer (largeur ≫ hauteur), long biseau
  progressif jusqu'à une proue pointue plutôt qu'un nez court.

## Pourquoi une tourelle de commandement et des tourelles d'armement ?

Même avec une coque anguleuse, "un seul fuselage + deux ailes" continue de se lire comme
"une fusée avec des ailes" — la topologie reste celle d'un avion. Casser cette lecture demande
de la vraie complexité *structurelle*, pas juste une silhouette différente :

- **`SuperstructureBuilder`** ajoute un pont/tourelle de commandement en 2 étages, posé sur le
  dessus de la coque — la référence directe étant le pont d'un Star Destroyer ou le CIC d'un
  Battlestar. C'est l'élément qui, à lui seul, fait le plus basculer la lecture "avion" vers
  "vaisseau capital assemblé". Absent pour les chasseurs (monoplace, pas de pont séparé).
- **Les tourelles** (`GreebleBuilder`, paramètre `TurretCount`) sont des greebles plus grosses
  et positionnées de façon déterministe (pas aléatoire comme les greebles ordinaires) : une
  rangée dorsale alternée qui donne une lecture "vaisseau armé" plutôt que "coque nue avec des
  décorations".
- **Les colliers de coque** (aux jonctions du profil) sont maintenant des anneaux structurels
  massifs plutôt que de simples traits lumineux fins — plus proches d'un anneau d'amarrage ou
  d'une bande de blindage.
- **Les ailes** sont plus épaisses (0.25+ au lieu de 0.15, une plaque structurelle plutôt qu'un
  profil aérodynamique fin) et portent maintenant deux blocs de panneaux en relief.

## Pourquoi des nacelles sur pylônes ?

Même avec une tourelle de commandement, la coque restait un **volume unique** — tout est
rattaché à un seul fuselage central. `NacelleBuilder` casse ça pour de vrai : une paire de
petites coques tubulaires (même algorithme de révolution en octogone chanfreiné que la coque
principale, via `HullBuilder.BuildVolume`, mais avec leur propre profil, plus simple) montées
sur des pylônes de part et d'autre de la coque, parallèles à son axe — la référence directe
étant les nacelles de propulsion de Star Trek, ou les pods de vol de Battlestar Galactica.
Contrairement à la tourelle (posée directement sur la coque), les nacelles sont **détachées**
de la coque principale et reliées uniquement par une structure fine (le pylône) : c'est ce qui
fait qu'un vaisseau ne se lit plus comme "un fuselage avec des trucs dessus" mais comme
"plusieurs volumes distincts assemblés". Nouveaux paramètres : `Nacelles` (bascule) et
`NacelleSize`.

## Étendre le générateur

- **Nouvelle classe de coque** : ajouter une entrée à `HullClassPreset.All` — une liste de
  `HullProfilePoint(U, Largeur, Hauteur)` (fractions de `Beam`, U de 0=nez à 1=poupe),
  `NoseFraction`/`TailFraction` (où les autres pièces considèrent le nez/la poupe "développés"),
  `Chamfer` (0=arêtes vives, ~0.4=quasi-octogonal), et un préfixe de désignation.
- **Nouveau style d'aile/moteur/cockpit** : ajouter une valeur à l'enum correspondant
  (`WingStyle`, `EngineStyle`, `CockpitStyle`) et le cas dans le builder associé ; l'UI
  (boutons segmentés) se met à jour automatiquement puisqu'elle itère sur `Enum.GetValues<T>()`.
  Penser à ajouter le libellé français dans
  [EnumLabelConverter](src/ShipDesign.App/Converters/EnumLabelConverter.cs).

## Prochaines étapes

- Une vraie section "soucoupe" séparée (comme la coque principale de Star Trek), en plus des
  nacelles déjà détachées — actuellement la coque principale reste un seul volume, seules les
  nacelles sont de vrais volumes secondaires.
- Plus de variété de greebles (antennes, dishes, panneaux de radiateur) au lieu du seul type
  "boîte" actuel.
- Undo / historique (actuellement, chaque changement régénère directement, pas d'annulation).
- Sélecteur de couleur libre (actuellement une palette de 8 préréglages par canal) plutôt
  qu'un simple ensemble de pastilles.
- Fenêtre sans chrome natif pour coller davantage au mockup (barre de titre Windows actuelle).

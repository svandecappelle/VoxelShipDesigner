# ShipDesign

Application desktop (.NET 7 / WPF) pour assembler des vaisseaux spatiaux à partir de pièces
modulaires (kitbashing procédural) et les exporter en `.glb` pour Unity.

## Structure

- `src/ShipDesign.Core` — modèles, chargement des pièces, moteur d'assemblage, export glTF.
  Ne dépend pas de WPF : réutilisable telle quelle (tests, CLI, etc.).
- `src/ShipDesign.App` — interface WPF (viewport HelixToolkit) qui utilise `ShipDesign.Core`.
  Sélecteur de template, champ de seed éditable (reproductibilité), liste des pièces
  assemblées avec édition manuelle (sélectionner une pièce pour la remplacer par une autre
  du même type, ou ajuster son échelle via un slider), boutons Régénérer (seed aléatoire)
  et Exporter (`.glb` via boîte de dialogue).
- `Assets/Parts` — bibliothèque de pièces. Chaque pièce est un fichier `.glb`/`.gltf`, avec
  un fichier `.json` optionnel du même nom pour ses métadonnées (catégorie, taille, tags).
- `Assets/Templates` — templates de vaisseaux, un fichier `.json` par template
  ([ShipTemplateLoader](src/ShipDesign.Core/Loading/ShipTemplateLoader.cs) les charge tous
  au démarrage ; voir "Ajouter un template" plus bas).
- `tools/PlaceholderPartGenerator` — génère des pièces "greybox" (boîtes) directement en C#,
  pour tester toute la chaîne sans dépendre de Blender. À terme, ces pièces seront remplacées
  par de vrais assets modélisés à la main.

## Lancer l'application

```
dotnet run --project src/ShipDesign.App
```

## Régénérer les pièces de test

```
dotnet run --project tools/PlaceholderPartGenerator
```

Écrit (ré)écrit `hull_fighter_01`, `hull_scout_01`, `wing_basic_01`, `wing_swept_02`,
`engine_basic_01` et `engine_heavy_02` dans `Assets/Parts/` (deux variantes par catégorie,
pour pouvoir tester le remplacement de pièce dans l'UI).

## Ajouter une pièce

1. Modéliser la pièce dans Blender. Les points d'ancrage sont des *empties* nommés
   `socket_<nom>` (ex: `socket_wing_L`, `socket_engine_R`). Un socket dont le nom se termine
   par `_R` est automatiquement mirroré sur X par l'assembleur : modélise la pièce (aile,
   arme...) une seule fois pour le côté `_L`, elle sera flippée pour son homologue `_R`.
2. Exporter en glTF (`.glb`) dans `Assets/Parts/`.
3. (Optionnel) Ajouter un fichier `.json` du même nom :

```json
{
  "category": "Hull",
  "sizeClass": "Medium",
  "tags": ["fighter"]
}
```

Catégories disponibles : `Hull`, `Wing`, `Engine`, `Weapon`, `Greeble`, `Cockpit`.

## Ajouter un template

Créer un fichier `.json` dans `Assets/Templates/` (voir
[fighter.json](Assets/Templates/fighter.json) pour un exemple) :

```json
{
  "name": "Bombardier",
  "hullPartId": "hull_fighter_01",
  "slots": [
    { "socketPattern": "wing_", "category": "Wing", "minCount": 2, "maxCount": 2 },
    { "socketPattern": "engine_", "category": "Engine", "minCount": 2, "maxCount": 2 }
  ]
}
```

`socketPattern` sélectionne les sockets de la coque dont le nom commence par cette valeur
(ex: `"wing_"` matche `socket_wing_L` et `socket_wing_R`). Il apparaît automatiquement dans
le sélecteur de l'application au prochain lancement.

## Générer un vaisseau (API Core)

```csharp
var library = PartLibrary.LoadFromDirectory("Assets/Parts");
var template = new ShipTemplate
{
    Name = "Fighter",
    HullPartId = "hull_fighter_01",
    Slots = new[]
    {
        new SlotDefinition { SocketPattern = "wing_", PartCategory = PartCategory.Wing, MinCount = 2, MaxCount = 2 },
        new SlotDefinition { SocketPattern = "engine_", PartCategory = PartCategory.Engine, MinCount = 1, MaxCount = 2 },
    }
};

var ship = new ShipAssembler(library).Assemble(template, seed: 42);
ShipExporter.Export(ship, "output/fighter.glb");
```

Le `.glb` généré s'importe directement dans Unity (glisser-déposer dans `Assets/`).

## Prochaines étapes

- Remplacer les pièces placeholder par de vrais assets modélisés dans Blender.
- Undo / historique d'édition (actuellement, seul "Régénérer" repart de zéro).

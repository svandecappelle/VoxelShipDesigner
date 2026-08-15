# ShipDesign

Application desktop (.NET 7 / WPF) pour assembler des vaisseaux spatiaux à partir de pièces
modulaires (kitbashing procédural) et les exporter en `.glb` pour Unity.

## Structure

- `src/ShipDesign.Core` — modèles, chargement des pièces, moteur d'assemblage, export glTF.
  Ne dépend pas de WPF : réutilisable telle quelle (tests, CLI, etc.).
- `src/ShipDesign.App` — interface WPF (viewport HelixToolkit) qui utilise `ShipDesign.Core`.
- `Assets/Parts` — bibliothèque de pièces. Chaque pièce est un fichier `.glb`/`.gltf`, avec
  un fichier `.json` optionnel du même nom pour ses métadonnées (catégorie, taille, tags).

## Lancer l'application

```
dotnet run --project src/ShipDesign.App
```

## Ajouter une pièce

1. Modéliser la pièce dans Blender. Les points d'ancrage sont des *empties* nommés
   `socket_<nom>` (ex: `socket_wing_L`, `socket_engine_R`).
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

- Ajouter les premières pièces réelles (coque + ailes + moteur) dans `Assets/Parts`.
- UI : liste de templates de vaisseaux + bouton "régénérer" (nouveau seed) + bouton "exporter".
- Édition manuelle : remplacer une pièce choisie, ajuster son échelle.

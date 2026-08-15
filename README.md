# ShipDesign

Application desktop (.NET 7 / WPF) pour assembler des vaisseaux spatiaux à partir de pièces
modulaires (kitbashing procédural) et les exporter en `.glb` pour Unity.

## Structure

- `src/ShipDesign.Core` — modèles, chargement des pièces, moteur d'assemblage, export glTF.
  Ne dépend pas de WPF : réutilisable telle quelle (tests, CLI, etc.).
- `src/ShipDesign.App` — interface WPF (viewport HelixToolkit) qui utilise `ShipDesign.Core`.
  Contient un template de vaisseau ("Fighter") câblé en dur, avec boutons Régénérer
  (nouveau seed) et Exporter (`.glb` via une boîte de dialogue).
- `Assets/Parts` — bibliothèque de pièces. Chaque pièce est un fichier `.glb`/`.gltf`, avec
  un fichier `.json` optionnel du même nom pour ses métadonnées (catégorie, taille, tags).
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

Écrit (ré)écrit `hull_fighter_01`, `wing_basic_01` et `engine_basic_01` dans `Assets/Parts/`.

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

- Remplacer les pièces placeholder par de vrais assets modélisés dans Blender.
- UI : liste déroulante de templates de vaisseaux (au lieu du seul "Fighter" en dur).
- Édition manuelle : remplacer une pièce choisie, ajuster son échelle.
- Mirroring des pièces symétriques (aile gauche/droite) au niveau des sockets.

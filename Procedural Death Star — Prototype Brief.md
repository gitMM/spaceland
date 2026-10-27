# Procedural Death Star — Prototype Brief

## Concept
Create a procedural, Death Star-inspired megastructure in Unity 6, using **Townscaper-style context-aware construction** on a spherical square grid.

The goal is a visually convincing industrial planet whose surface is automatically assembled from modular architecture.

## Core System

1. **Cube Sphere:** Subdivide all six faces of a cube into square grids, then project them onto a sphere.
2. **Modular Architecture:** Generate armor plates, buildings, trenches, vents, towers and mechanical details from reusable geometric modules.
3. **Neighbor-Aware Generation:** Adjacent tiles influence each other's geometry. Buildings connect, walls merge, trenches continue, and transitions happen automatically, including across cube-face boundaries.
4. **Procedural Variation:** Use seeded generation to create different districts, structures, heights and detail densities. Preserve large recognizable features and quieter regions.
5. **Interactive Construction:** Allow the user to paint, raise, lower and erase structures on the sphere. Neighboring geometry automatically adapts, similar to Townscaper.

## Art Direction
- Monochrome, brutalist, industrial science fiction.
- Dense mechanical detailing contrasted with large, relatively smooth armor plates.
- Strong rectangular geometry, recesses, trenches and interconnected structures.
- Planet-scale architectural features recognizable from orbit.

## Inspirations & References

**1. Townscaper — Oskar Stålberg**

https://store.steampowered.com/app/1291340/Townscaper/

Reference for intuitive construction, neighbor-aware modular geometry, automatic architectural transitions and beautiful results from simple player interactions.

**2. Polygonal Planet Project — Oskar Stålberg**

https://oskarstalberg.com/game/planet/planet.html

Technical discussion:
https://polycount.com/discussion/163125/polygonal-planet-project-a-unity-demo-for-your-browser/p1

Reference for procedurally assembling handcrafted geometric modules across a spherical world, adapting architecture to curved surfaces, and interactive planet editing. Our implementation uses a **cube sphere instead of an icosphere**.

**3. Star Wars — Death Star**

Visual reference for enormous industrial surfaces, paneling, trenches, mechanical density, recessed structures and monumental scale. Create an original design inspired by this architectural language.

## Prototype Scope
- One complete procedural sphere.
- Several distinct procedural architectural module types.
- Interactive editing with automatic neighbor connections.
- Orbit camera with smooth zoom.
- Seed-based regeneration.
- No interiors or gameplay systems.

## Success Criteria
**A beautiful, explorable, editable mechanical planet** that feels like Townscaper applied to Death Star architecture.

The geometry must look deliberately engineered rather than randomly scattered across a sphere.

Prioritize convincing procedural construction and visual quality over feature count.
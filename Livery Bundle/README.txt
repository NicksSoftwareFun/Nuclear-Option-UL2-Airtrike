UL-2 Airtrike Livery Bundle

Everything you need to paint a livery for the UL-2 Airtrike in Blender (or any 3D paint program).
Matches mod version 1.0.0 and later.

FILES

UL2 Exterior.fbx
  The complete exterior, assembled, in metres, with the in-game texture layout. Two objects:
  - "UL2 Skin": every surface that takes the livery, as ONE object: the pod, nose, gun mount, tail fin, rudder,
    tailplanes, elevators and their tips. Paint this one.
  - "UL2 Details": frame, mast, rotor, engine, propeller, seats, controls, gear and tyres. A livery does not paint these.
  The skin uses material UL2_skin, which points at "UL2 Base.png". Keep the FBX and the PNG files in the same folder.
  Hidden inside faces are left out.

UL2 Base.png
  Blank base colour: plain white with faint panel lines, no markings, 2048 x 2048. Paint over this or over one of
  the shipped liveries.

UL2 UV Layout.png
  Texture island outlines on a transparent background, to lay over your work in a 2D editor.

UL2 Normals.png, UL2 Metallic.png, UL2 Roughness.png, UL2 Metallic Smoothness (game format).png, UL2 AO.png
  The shared surface maps, for previews. A livery does not change these.

Shipped Liveries
  The five liveries that ship with the mod: Reservist Olive, Stainless Steel, Black Tiger, PALA Camel Corps and
  BDF Orca.

Example
  "Racing Navy.png" is a test livery painted only from this bundle's FBX, and "Racing Navy in game.jpg" shows it on
  the game model: the stripes run straight across every panel joint.

PAINTING IN BLENDER

1. File > Import > FBX, pick "UL2 Exterior.fbx". The aircraft appears with the base colour on the skin.
2. Select "UL2 Skin", switch to the Texture Paint workspace and paint on "UL2 Base" (or open a shipped livery
   in the Image Editor and swap it into the UL2_skin material's image node). Blender only paints the active
   object, and the whole skin is one object, so a fill or a brush stroke reaches every panel.
3. Image > Save As, PNG, 2048 x 2048, sRGB.
4. Upload it as a Workshop livery with the tag "UL-2 Airtrike".

GOOD TO KNOW

- A livery only replaces the base colour. Panel lines, rivets and weathering in the shared maps stay on top of
  your paint.
- The skin is double-sided in the game, so the inside of a broken panel shows your paint mirrored and in shadow.
  Nothing extra to paint.
- Keep important artwork a few pixels away from the island edges.

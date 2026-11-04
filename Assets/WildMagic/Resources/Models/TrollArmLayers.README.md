# Troll arm layer prototype

`TrollArmLayers.fbx` is a generated technical prototype for the layered creature-damage pipeline.

The prefab contains three aligned meshes:

- `Arm_L_Skin` — exterior surface used for the first damage layer;
- `Arm_L_Flesh` — simplified closed inner volume;
- `Arm_L_Bone` — a stylized internal skeleton.

The source sculpt was generated locally with Hunyuan3D and prepared in Blender. It is not a final production asset: the topology, anatomy and rig still require a dedicated game-ready pass. The Unity prototype stores wound positions in each mesh's object space so the damage remains attached after the segment becomes a physics object.

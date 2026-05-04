# Asset Pipeline Fixtures

`tiny_rotor.obj` and `tiny_rotor.anim.json` are intentionally tiny assets for
fast converter smoke tests. They cover:

- one static mesh group;
- one animated mesh group;
- one looped rotation clip;
- single-model and chunked export paths.

The test script generates `tiny_rotor.png` locally so the repository does not
need a binary fixture just to exercise PNG size detection.

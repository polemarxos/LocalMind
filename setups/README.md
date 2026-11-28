# Recommended setups

LocalMind reads these files when it starts for the first time (and offers newer versions later):

- `8gb.json`: graphics cards under 12 GB (and PCs without one)
- `16gb.json`: 12 GB and more

To make your own: install the models you want, make your profiles (Model Manager → Profiles), then use
**Models → Export my setup…**. Put the file here with the right name and raise `Version` so users who
installed an older one are offered the new models.

Chat models (`llm`) use `"Quant": "Auto"` so each PC gets the size that fits its graphics card; picture and video
models (`media`) get the largest edition that fits, whatever `Variant` says.

## Introduction

This one's for all the **Rex** players out there. By default, all it does is prevent **Eclipse 8**'s *permanent curse* debuff from being applied when spending health on ability costs. In addition to the obvious implications for *Seed Barrage*, *Bramble Volley*, and *Tangling Growth*, this also affects corrupted **Void Fiend**'s special skill.

However, configuration options are also available for other forms of self-inflicted damage such as friendly fire (e.g. *OGM-72 'DIABLO' Strike*), fall damage, and interactables like *Shrine of Blood*. An optional *Artifact of Infliction* can be enabled in both the **Eclipse** lobby and other game modes to allow players to easily toggle the above functionality.

## Known Issues

- None currently. 

Please report any feedback or issues discovered [here](https://github.com/6thmoon/CurseCatcher/issues). Feel free to check out my other [work](https://thunderstore.io/package/6thmoon/) as well.

## Version History

#### `0.2.0`
- Update transpiler logic for compatibility with other plugins that modify the same section of code.
	- Now affects curse applied by *Artifact of the Eclipse* (from [`ZetArtifacts`](https://thunderstore.io/package/William758/ZetArtifacts/) & [`DiluvianArtifact`](https://thunderstore.io/package/William758/DiluvianArtifact/)), and the *Artifact of Eclipse 8* introduced in [`EclipseArtifacts`](https://thunderstore.io/package/Judgy/EclipseArtifacts/).
- Fix issue when reading enabled artifacts as multiplayer client.

#### `0.1.2` ***- Initial Release***
- Prevent curse on self-damage. Host-only multiplayer support is implemented, but has not been tested extensively.

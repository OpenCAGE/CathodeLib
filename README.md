# CathodeLib

An open source library providing functionality to parse and write various formats from the Cathode engine, used for modding Alien: Isolation.

## Supported files & formats

* COMMANDS.PAK
  * The Commands file forms the scripted logic for a game's level. This logic was created through a node-based scripting system similar to Blueprint in UE, and can be parsed using CathodeLib's `CATHODE.Commands.CommandsPAK` class. A `CommandsPAK` is made up of `CathodeComposite` objects (composites essentially being scripting functions), each containing various types of `CathodeEntity` objects (entities being nodes to script logic within the functions). Each `CathodeEntity` contains various information, with all containing `CathodeParameter` objects, which specify additional parameters on the scripting node (e.g. `position`, etc).

* MODELS.MVR
  * The Mover file is a legacy format carried over from Viking: Battle for Asgard (a game which used an early version of Cathode), however it is still heavily utilised in Cathode alongside the Commands system. The Mover file sets up all Movers in a level, where a "Mover" is an instanced entity, containing a model and material/position data. Movers are given a type enum value which specifies how much data is used from the MVR file, and how much comes from the Commands.PAK file. Each Mover is defined in a `MoverEntry` object, however considerable work is still required to understand the Mover description completely.



**still writing this...**

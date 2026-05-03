# Banana Time

![a plantain](BananaTime/Content/Graphics/Banana.png)

this game was started _intending_ to submit it to the <a href="https://itch.io/jam/mini-jame-gam-54">Mini Jame Gam #54</a>, but after a few hours I realized and remembered I had other things to do this weekend, so dropped it 

I considered deleting this repo, but then thought "I dunno - I did some interesting things. maybe someone would want it." so here it is.

#### tech stack

* C#/MonoGame/PlayPlayMini
* Aether.Physics2D

#### gameplay

* be a plantain doing difficult platforming over Stone Henge, I guess?
* WASD, arrow keys, numpad, and gamepad support
* rewind time feature - press R to rewind time 6 seconds (with a fun VCR pixel shader)

#### level editor

* left click to add polygon points
* right click to remove the last point in the current polygon
* toggle polygon between being a surface or a kill zone with `K`
* "export" to JSON (writes it to the console) with `X`
* copy exported JSON into level file to use it in-game

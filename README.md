# GridGame
A web server game. A small experiment written over xmas, might eventually be expanded to support plugin style games.

~~Depends on https://sparkjava.com version: `spark-core-2.3.jar` downloaded to the libs directory~~ I'm lazy and just included all the jars in the repo. TODO clean that up some day (but I know I probably won't bother)

This is very barebones and has been built & run entirely on command line, e.g.

`javac -sourcepath src src/Server.java --class-path lib/* -d out`

And run:

`java -classpath "lib/*;out" Server`

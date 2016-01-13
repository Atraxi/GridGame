import spark.Spark;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.*;

public class Server
{
    private static final Map<Integer, Game> games = Collections.synchronizedMap(new HashMap<Integer, Game>());

    public static void main(String[] args) throws IOException {
        String indexHTML = new String(Files.readAllBytes(Paths.get("index.html")));
        String createHTML = new String(Files.readAllBytes(Paths.get("create.html")));

        Spark.port(80);

        Spark.get("/create-submit", (request, response) ->
        {
            Game newGame = new Game(request.queryParams("name"), Integer.valueOf(request.queryParams("size")));
            games.put(newGame.hashCode(), newGame);
            //response.redirect("/game/" + newGame.hashCode());
            response.redirect("/");
            return "success";
        });

        Spark.get("/create", (request, response) ->
        {
            return createHTML;
        });

        Spark.get("/game/:gameID/:player/:x/:y/:xOffset/:yOffset", (request, response) ->
        {
            Game game = games.get(Integer.parseInt(request.params(":gameID")));
            game.update(Integer.parseInt(request.params(":x")), Integer.parseInt(request.params(":y")), request.params(":player"), Integer.parseInt(request.params(":xOffset")), Integer.parseInt(request.params(":yOffset")));
            return game.draw(request.params(":player"));
        });

        Spark.get("/game/:gameID/:player/:x/:y", (request, response) ->
        {
            Game game = games.get(Integer.parseInt(request.params(":gameID")));
            game.update(Integer.parseInt(request.params(":x")), Integer.parseInt(request.params(":y")), request.params(":player"), 1, 1);
            return game.draw(request.params(":player"));
        });

        Spark.get("/game/:gameID/:player", (request,response) ->
        {
            return games.get(Integer.parseInt(request.params(":gameID"))).draw(request.params(":player"));
        });

        Spark.get("/game/:gameID", (request, response) ->
        {
            return "";
        });

        //default route, home page
        Spark.get("/", (request, response) ->
        {
            StringBuilder gameList = new StringBuilder();

            ArrayList<Game> gamesList = new ArrayList<>(games.values());
            Collections.sort(gamesList);
            for(Game game : gamesList)
            {
                gameList.append(
            "			" + game.getName() + "\t<a href=\"/game/" + game.hashCode() + "/green\">Green</a>\t<a href=\"/game/" + game.hashCode() + "/red\">Red</a>\n" +
            "			</br>\n"
                );
            }
            return indexHTML.replace("<!--INSERT_GAME_LIST-->", gameList.toString());
        });
    }

    public Game getGame(int gameHash)
    {
        return games.get(gameHash);
    }

    public Game createGame(String name, int size) throws IOException {
        Game newGame = new Game(name, size);
        games.put(newGame.hashCode(), newGame);
        return newGame;
    }

    public void EndGame(int finishedGameHash)
    {
        games.remove(finishedGameHash);
    }
}
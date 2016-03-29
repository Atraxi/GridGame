import org.eclipse.jetty.websocket.api.Session;
import org.eclipse.jetty.websocket.api.annotations.OnWebSocketClose;
import org.eclipse.jetty.websocket.api.annotations.OnWebSocketConnect;
import org.eclipse.jetty.websocket.api.annotations.OnWebSocketMessage;
import org.eclipse.jetty.websocket.api.annotations.WebSocket;

import java.io.IOException;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Created by Atraxi on 17/01/2016.
 */

@WebSocket
public class SocketGameNetworkLayer {
    private static HashMap<Session, Game> games = new HashMap<Session, Game>();

    @OnWebSocketConnect
    public void connect(Session session)
    {
        System.out.println("Connection attempt received");

        Map<String, List<String>> paramMap = session.getUpgradeRequest().getParameterMap();

        int gamehash;
        try
        {
            gamehash = Integer.parseInt(paramMap.get("gamehash").get(0));

        }
        catch (Exception e)
        {
            try
            {
                session.getRemote().sendString("Error accessing or parsing gamehash");
            }
            catch (IOException e1)
            {
                e1.printStackTrace();
            }
            return;
        }

        Game game = Server.getGame(gamehash);
        if(game != null)
        {
            games.put(session, Server.getGame(gamehash));
        }
        else
        {
            try
            {
                session.getRemote().sendString("Could not find existing game with that hash");
            }
            catch (IOException e1)
            {
                e1.printStackTrace();
            }
            return;
        }
    }

    @OnWebSocketClose
    public void closed(Session session, int statusCode, String reason)
    {
        System.out.println("Session closed, code:" + statusCode + " reason:" + reason);
        Server.EndGame(games.get(session).hashCode());
        games.remove(session);
    }

    @OnWebSocketMessage
    public void message(Session session, String message)
    {
        System.out.println("received:" + message);
        try {
            session.getRemote().sendString("{\"received\":\"" + message + "\"}");
        } catch (IOException e) {
            e.printStackTrace();
        }



        Game game = games.get(session);
        //game.update();
    }
}

package helloakka;

import akka.actor.ActorRef;
import akka.actor.ActorSystem;
import helloakka.actors.WorldActor;
import helloakka.messages.HelloAll;

public final class Main {
    public static void main(String[] args) throws Exception {
        ActorSystem system = ActorSystem.create("HelloAkka");
        try {
            ActorRef world = system.actorOf(WorldActor.props(), "world");
            world.tell(new HelloAll(), ActorRef.noSender());
            Thread.sleep(1000);
        } finally {
            system.terminate();
        }
    }
}

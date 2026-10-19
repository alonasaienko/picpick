"""Watermark worker: читає задачі з RabbitMQ і робить прев'ю з вотермаркою."""
import logging

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("worker")


def main() -> None:
    log.info("Worker started")


if __name__ == "__main__":
    main()

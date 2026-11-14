from pathlib import Path

from diagrams import Cluster, Diagram, Edge
from diagrams.aws.compute import Fargate
from diagrams.aws.database import RDS
from diagrams.aws.integration import MQ
from diagrams.aws.management import AmazonManagedGrafana, AmazonManagedPrometheus, Cloudwatch
from diagrams.aws.network import ALB, NATGateway
from diagrams.aws.security import SecretsManager
from diagrams.aws.storage import S3
from diagrams.generic.blank import Blank


output = Path(__file__).resolve().parent / "img" / "aws-deployment"
output.parent.mkdir(parents=True, exist_ok=True)

with Diagram(
    "PicPick · AWS reference deployment",
    filename=str(output),
    outformat="png",
    show=False,
    direction="LR",
    graph_attr={"bgcolor": "white", "pad": "0.3", "dpi": "120", "splines": "polyline"},
):
    browser = Blank("Browser\nPhotographer / Client", shape="box", style="rounded", labelloc="c", width="1.9", height="0.85")
    smtp = Blank("External SMTP", shape="box", style="rounded", labelloc="c", height="0.6")

    with Cluster("AWS Region"):
        with Cluster("VPC · multiple Availability Zones"):
            with Cluster("Public subnets · at least two AZs"):
                alb = ALB("Application Load Balancer")
                nat = NATGateway("NAT Gateway\n1 per public AZ")

            with Cluster("Private application subnets"):
                web = Fargate("Web App · ECS Fargate\n2–4 tasks")
                worker = Fargate("Worker · ECS Fargate\n1–10 tasks")
                collector = Fargate("Metrics collector\n+ postgres_exporter")

            with Cluster("Private data subnets"):
                db = RDS("RDS PostgreSQL\nprimary + standby")
                broker = MQ("Amazon MQ\nRabbitMQ · 3 nodes")

        photos = S3("Private S3 buckets\noriginals and previews")
        deletion_log = S3("Deletion journal\nObject Lock")
        secrets = SecretsManager("Secrets Manager")
        monitoring = Cloudwatch("CloudWatch\nlogs and scaling")
        prometheus = AmazonManagedPrometheus("Managed Prometheus\n+ Alertmanager")
        grafana = AmazonManagedGrafana("Managed Grafana")

    browser >> Edge(label="HTTPS") >> alb >> Edge(label="HTTP") >> web
    web >> Edge(label="SQL / TLS") >> db
    web << Edge(label="AMQPS", style="dashed") >> broker
    worker << Edge(label="AMQPS", style="dashed") >> broker
    worker >> Edge(label="Internal HTTP", constraint="false") >> web
    web >> Edge(label="S3 API") >> photos
    worker >> Edge(label="S3 API") >> photos
    web >> Edge(label="SMTP / TLS", constraint="false") >> nat >> smtp
    web >> Edge(label="Append") >> deletion_log
    web >> Edge(style="dotted") >> secrets
    for source in (web, worker, db, broker):
        source >> Edge(style="dotted", label="Metrics") >> collector
    collector >> Edge(style="dotted", label="remote_write") >> prometheus
    prometheus >> Edge(style="dotted", label="Dashboard data") >> grafana
    for source in (web, worker):
        source >> Edge(style="dotted", label="Logs") >> monitoring
    broker >> Edge(label="Queue metrics", style="dotted") >> monitoring

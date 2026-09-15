# Usa a VPC default da conta/região para manter a infra simples (sem NAT gateway, sem subnets
# customizadas) — adequado para uma instância única. Se a conta não tiver VPC default, crie uma
# (aws ec2 create-default-vpc) ou troque estes data sources por uma VPC própria.

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}

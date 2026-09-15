# Infraestrutura AWS — CSS Brasil CRM

Terraform para uma instância EC2 única rodando o CRM em Docker, atrás do Caddy (HTTPS automático),
com RDS Postgres, S3 para anexos, e Secrets Manager para segredos.

## Por que não usa o certificado ACM

Você mencionou já ter domínio (`cssbrasil.duckdns.org`) e um certificado ACM — mas os dois não se
combinam bem com uma instância EC2 "crua":

- **ACM só funciona com serviços gerenciados pela AWS** que fazem a integração nativa (ALB,
  CloudFront, API Gateway). A chave privada de um certificado ACM não é exportável, então não dá
  pra instalar num nginx/Caddy rodando numa instância EC2 comum.
- **DuckDNS só aponta pra um IP fixo** (registro A) — não suporta apontar pra um nome de host tipo
  o DNS de um Application Load Balancer, que muda de IP.

Juntando as duas restrições, o caminho mais simples e mais usado nesse cenário exato (domínio
dinâmico + instância única) é: Elastic IP fixo na instância + Caddy emitindo e renovando um
certificado **Let's Encrypt** sozinho pro domínio, via desafio HTTP-01 (a instância só precisa da
porta 80 aberta, que já está). Sem custo, sem gerenciamento manual de renovação.

Se no futuro você quiser usar o certificado ACM de verdade, o caminho é colocar um Application
Load Balancer na frente da instância (ACM pluga direto nele) — nesse caso o DuckDNS teria que ser
trocado por um domínio num provedor que suporte CNAME (ou Route 53).

## O que isto cria

- Instância EC2 (Amazon Linux 2023) rodando o CRM via Docker Compose (app + Caddy).
- Elastic IP (novo, ou associa um já existente — ver `elastic_ip_allocation_id`).
- RDS PostgreSQL (privado, só a instância do CRM acessa).
- Bucket S3 para os anexos enviados pelos usuários.
- Repositório ECR para as imagens Docker.
- Secrets Manager: um segredo com a connection string do banco (gerado automaticamente) e outro
  para SMTP/Meta Lead Ads/CAPI (você preenche os valores depois do apply).
- IAM role da instância com acesso só ao necessário (o bucket, os dois segredos, ECR, SSM).

Usa a VPC default da conta pra manter simples — sem NAT gateway, sem subnets customizadas.

## Pré-requisitos

1. [Terraform](https://developer.hashicorp.com/terraform/install) >= 1.5 e a [AWS CLI](https://aws.amazon.com/cli/) instalados e configurados (`aws configure`) com um usuário/role que tenha permissão de criar os recursos acima.
2. Um Key Pair EC2 já criado na região escolhida (Console EC2 → Key Pairs → Create key pair).
3. Seu IP público atual, para liberar o SSH (`curl ifconfig.me`).

## Como aplicar

```bash
cd infra/aws
cp terraform.tfvars.example terraform.tfvars
# edite terraform.tfvars: domain_name, admin_cidr, key_pair_name, elastic_ip_allocation_id

terraform init
terraform plan   # confira o que vai ser criado antes de aplicar
terraform apply
```

Depois do primeiro apply:

1. Se `elastic_ip_allocation_id` ficou em branco, pegue o IP alocado (`terraform output public_ip`) e atualize o DuckDNS pra apontar pra ele.
2. Preencha os valores reais do segredo de app (SMTP, Meta Lead Ads/CAPI):
   ```bash
   aws secretsmanager put-secret-value \
     --secret-id "$(terraform output -raw app_secret_name)" \
     --secret-string file://segredo-preenchido.json
   ```
3. A primeira imagem ainda não existe no ECR nesse ponto — rode o workflow de CI/CD (push na
   branch principal) pra construir e publicar a primeira versão, depois dispare o deploy (ver
   `.github/workflows/deploy.yml`).

## Destruir

```bash
terraform destroy
```

O RDS tem `deletion_protection = true` — desative essa flag num `apply` antes de conseguir
destruir, se um dia precisar.

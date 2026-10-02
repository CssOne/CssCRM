using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <summary>
    /// Desfaz a migração UnificaContasLarissaSabrina: as contas "Larissa" e "Sabrina" com e-mail
    /// @cssvision.local NÃO eram contas provisórias delas — eram as contas de "vendedor não
    /// identificado" das regionais CSS Growth Sales e MG132 (renomeadas), com os leads do Notion sem
    /// vendedor e de outros vendedores. A junção levou tudo isso para as contas reais.
    /// <para>
    /// Volta para a conta "não identificado" de origem tudo o que está na conta real e NÃO era dela no
    /// backup de 02/10/2026 03:00 (antes da junção), exceto leads cujo vendedor no Notion é a própria
    /// pessoa (esses ficam com ela) e o que foi atribuído depois da junção. As vendas acompanham o lead.
    /// Os nomes das contas voltam a ser os de antes.
    /// </para>
    /// </summary>
    public partial class ReverteJuncaoContasNaoIdentificado : Migration
    {
        private const string Juncao = "2026-10-02 12:04:00+00";

        private static readonly (string Real, string NaoIdentificado, string NomeAnterior, string[] LeadsProprios, string[] VendasProprias)[] Contas =
        [
            ("01a0a5e9-01dd-7b72-be60-be586c5a2ee4", "01a0a5e9-03a9-72eb-88f3-c232dbc22ce0", "Larissa",
            [
                "00b855c4-1b8f-45b0-8d19-08d7f484b36e", "0116cd94-3336-49e5-9965-fa5931e859d9", "027174f2-58b3-474e-9c65-e7fb21a6a908",
                "0453f629-cb1a-4a20-8fe6-79b05d88a46d", "0743f242-2f3a-40af-8325-cec09aa9a42d", "08d364bc-aae2-46d2-9177-200289f74d04",
                "09525469-deb3-4788-bcf1-eaaac4e1b712", "0a0d5b09-cec8-431e-8b15-42fbb2c5c87a", "0a419a65-0b05-4abb-aa93-8f13c2c4a988",
                "0a7267a7-ea71-4941-b88a-2c787840c17d", "0cc4bdbe-8baf-4acf-8568-0ea4fe1e88df", "0d5a4ba0-da5b-4bc9-83fe-42f206e6fb90",
                "101e1b64-a5ed-43f6-9d5d-29a9500f6bcb", "13421ccc-0b16-4c85-9f64-30a41b99592a", "14695d08-b2fd-40c6-ac74-823ac46e0c97",
                "15b1dd99-fcf5-46ba-9427-2bc8a8d8cc02", "16af4582-6711-4adc-810e-b032c5fa978b", "1dea519b-b038-42f9-a340-9007869342e9",
                "1e02696a-ddcf-4e25-9ec4-dec6092bb9c8", "1e23abfc-8c12-4d36-bba2-af5a13a30083", "1fa02753-668b-44a7-a323-3d7e5aaf4243",
                "259eb048-bb5f-4e06-a7ac-3034c6f9f4d4", "28323c5f-0347-46d8-983f-f20392308819", "289815ba-f863-4737-b6f8-ff3e42f6afd6",
                "28a818ba-ef90-4929-8975-ea5ad96871c1", "28f2faad-afc2-4861-9208-0e3267925e56", "2e6088e4-0384-4543-8621-529627eff01b",
                "2e796697-0a97-4c9b-ad78-fa1267b4805d", "2f216c92-d2ce-4a62-bf9a-cc2eb09c731b", "2fc04423-cf23-4e60-957a-b9a47d2223ab",
                "31a3eb3a-37d9-40e9-b049-ebc3139f6df2", "33785308-22ec-403e-8990-30e0b609a973", "376fdc2c-4461-4319-9276-7689d22adf05",
                "37cfdd36-6180-4ce1-a2a9-9b8024b0111b", "39f7ceb9-3365-499e-984f-75c839c3d415", "3b05b5e1-c59b-4f8f-bdb1-8098bfddbbb6",
                "3c64d94c-5319-418a-8c5f-6d6da839769b", "3d172bdc-39ad-4a02-9345-ad51c1759785", "3e40b3eb-6698-4482-a34d-0f8e7032915b",
                "3f8a9637-5aaf-450b-981c-963e8ae5af29", "3fa74dd1-56e7-4f69-8eb7-434c5d782ae4", "400be9bb-d1ea-434f-8e8b-9ca869a1c2ff",
                "44326d66-225a-454a-8847-d7490a76e10c", "4739062d-b38b-45d5-9e4c-8a30bdac278b", "47410836-a052-4a22-9d43-f6dc3235a6f5",
                "4853d631-9816-44c6-af07-36be591f731e", "48b10df9-ba35-482e-a8bf-7679ffbb28ac", "48ed48ba-f56c-4378-a51d-2e18b6172fda",
                "490ffa03-842d-4481-b193-37f9bcfb5fbb", "49597cd8-bee2-4c29-a93a-273099398d7d", "4ad4b756-d99a-4f45-99ca-943ac269bf2c",
                "4c628ba4-916d-48fe-acde-c5407cf0d788", "4d75b962-4cb3-474b-afe0-113a53e67874", "4f309dc5-a2f1-46e4-8066-ffc33d53b2e0",
                "54f5fdff-a122-4176-af0b-4ad923980754", "552d6805-029f-4e85-b00f-8b3387b8582a", "57971254-4641-4d5b-b412-df4c6f20d8ed",
                "5ca690da-f439-4687-999a-e9484600be12", "5ecefbac-d0dd-46a9-8527-66ffdba9d16c", "60f3f8a1-c2b9-4da3-814d-9269eae11618",
                "62f8e58c-8eeb-4d90-9573-e35bf6f3148a", "64315379-f397-4fc3-873f-33ea650e3e66", "67a16117-725d-46c5-ba2d-5ce8464710c4",
                "68d844be-dcf7-46f9-bd68-76dacd9cda9f", "69782a13-36b6-4488-8b5b-f28b648eb706", "6aa3f305-4548-4a12-b92c-6d5825c9926a",
                "6acfc212-bcb9-4c6f-b04f-98a69c100a04", "6ae46f1c-8367-4c95-ac90-822fdf5d5b1b", "6c5212b3-43fc-4152-9091-cd81362512d0",
                "6c628f2f-6a4c-4872-8376-00d1850d15e3", "6c9c7c76-e773-41cf-8c63-e0b588f49083", "6cee5232-ee1d-4192-a3e5-436063b8b784",
                "6d0fabda-3dea-4d6b-ac6f-5204f1c81387", "6dce17b2-b346-4287-8d6e-3538696c56e1", "6f981ab0-9887-4622-bef1-ec0a31185530",
                "6fd5e651-3ee8-42bf-b2e2-5919fd1a968b", "7233dd00-677d-4c99-8644-fb3578a15300", "72f1971b-7ebd-4c4e-a8aa-b07e721270be",
                "738e5eef-3e42-42d1-a809-f66386fe25ab", "74e77179-1cda-4254-80c5-9a161c013a2a", "755d20e1-e621-46f2-a802-f87fb81528ee",
                "757eefe4-ebd8-45ad-9354-d42b6ea4f13d", "75c7448f-85d1-4b5c-a912-8d36eb51344d", "7797c5d3-27bc-4294-bed5-371e30784497",
                "78175ff0-91d2-48dd-910a-5162d3c67f0e", "78a856fd-8eb0-4499-8a66-ab184cd9008c", "79e0a109-22a0-443b-9a0b-3c7c55c1b542",
                "7c43165b-2de2-42c4-bec9-922896eb56f9", "7cff1e35-dac2-4725-9be7-8f8fc610f970", "7e35f825-7a6c-409a-98de-a2e61b9de242",
                "7fc56a24-7159-4560-bcb3-9aff24c9f634", "8170e54e-c116-4e91-b2d4-6b1cafe11afd", "8198e849-0d9c-4ecb-8517-9376592e049d",
                "8206b6ab-0373-4070-b3e5-1c26ccd9baa7", "83c28361-ed87-4c99-9eb4-4d40d6527237", "866ef877-3299-4a19-b903-e0ddb0baffb7",
                "8e31489e-3c0c-4763-8207-8d9145125287", "8f000ffe-9ece-487f-86ae-0cbeff54edeb", "8f64bdf6-b899-419a-aff2-1f931efb2b75",
                "94882b01-6203-4e90-aeee-ab279c686a47", "94e3d3c6-aee9-4e13-878b-2d2bf9f12cca", "9755624f-9663-441a-86e4-78fa237dc5eb",
                "98a783d2-ac8d-42fa-88df-20bad14292ad", "99ea8913-52b6-4253-9f84-2f4e08b2a93d", "9a3ef913-606b-45f3-ac66-50db7829bfaa",
                "9a8a97b3-3593-4182-b940-f4af9defae47", "9e1bc70f-0627-4cc6-9680-abd889a30bd1", "a0bc8982-398d-44df-a8c7-d9a308a778d5",
                "a42d50bb-2135-4cdb-8515-ac29ab1a7206", "a4df27ed-0ab9-4354-bec1-04a758dac8a7", "a5bd2ba1-805a-4da6-a852-c6b60778d444",
                "a82e725f-61ce-4314-ba26-bc57e801738d", "ab351e9c-f889-40a6-ab6a-39fa4e9e168c", "ac7d897e-f965-43aa-adab-7f7cb1ba3b40",
                "aef84a49-cb19-4750-91d7-bebe9355c739", "b1379f7d-756e-4f5a-92f1-50565b3f2a5c", "b26a71e8-27c9-421f-aef2-0c31c84c867f",
                "b4444032-d46b-4cd1-a711-803490e78f90", "b853be86-eb6f-44fa-bbdc-32bf9d4256b6", "bc4e61c3-6b7d-4f46-bfb0-d2307b4f92be",
                "bce7bc57-7603-4484-b44b-3102aea9f732", "bd3131b9-e06a-46a4-a1c5-884786bb46a3", "c114293d-5142-43fb-9048-04e704e3fed7",
                "c1d3c47e-1cc9-4e31-a713-71cea6222a44", "c414d024-0641-4b8e-aba8-7f362d442ab7", "c4687e38-d157-406d-8ee9-5fdab5c0eb55",
                "c4a81f2c-48ed-4602-8990-284f60802075", "c64b8929-6d7b-4d19-9fea-8ea12e5ed22f", "c685fb45-f40b-4e41-b7eb-f2858d095a9a",
                "c6c39fd6-2227-4af1-87ac-c4714ef7a892", "c7cbb71e-b81b-4e54-ad5d-ae361a78f31d", "c7e820ef-733f-4c48-8ef8-c51c45cb43ce",
                "c956e619-70db-4d1d-9893-53b243500169", "c991b3a5-e804-4499-8dc5-c24a8e4c3b15", "cbb882ff-9c42-4c84-9c13-39ecd0bbc20f",
                "cc7c92d7-1c18-423d-b7df-90bb985d5cea", "ccb3d4b3-229c-414b-b89d-04d3b06722ed", "ce0c1ca4-924d-437b-aa95-d926518cd068",
                "ce1f1b9d-1dfd-4111-aa4b-d3e1f44e9346", "ce9dea7f-2e37-4e8f-a4d6-08e6dfe13a95", "d1329baa-0f43-4b19-b055-e68300679bd3",
                "d14f093e-835f-4da1-8d1d-c716814e38bb", "d168029f-34c5-4f3f-a638-8fca350d5ca6", "d2a1f8a1-9a3c-43de-b1e1-b227aaf503a3",
                "d439be26-aab0-45ae-a103-5ca8659ed890", "d512ebd5-3346-4137-83af-c5390ef919c8", "d5f1f83f-f5c2-43cf-a934-32f47c94f751",
                "da9c9afe-fa07-4638-8084-84a635b37238", "dafea3c4-fd33-4f3b-846c-bda06d73c33a", "db708c77-2a3e-4a9d-bcca-5b05febe6b64",
                "dd32b7ed-7b31-45b3-b22e-864959bcc04a", "dd40d710-f1da-4d8d-94df-0a44c4da97c3", "dd87df66-ae8d-40e8-a647-9df2b6cea2c6",
                "defd7fde-f9e9-43b6-b68f-7e084ad2bcff", "e0179c9c-2351-4a1d-b80d-b90cf82310b6", "e0865ca0-5a12-42e1-9526-35ad477b53b0",
                "e0b908b6-39e1-4551-9fb6-b036b2730c45", "e3320b70-2058-4ea1-8717-3ef9bad95e90", "e4e7f0a4-49c9-4933-a91a-1527c23ed699",
                "e59066ed-103c-4e09-84ad-53409da0642d", "e5a06f15-b74b-4653-a520-54255029237c", "e85afe90-e8ad-4952-937e-ab329cf4e143",
                "e8db1cba-e111-4a64-a411-3a1da12b003c", "ea9dda14-13c9-4c2d-bbfc-ab1bbf5bb6a5", "ed8941dd-e020-44de-b94a-1b8f96eba0c2",
                "f13a505f-8309-4511-8205-0f112b0b71c3", "f21f8c5d-cb77-451b-b8f1-a33efba9419f", "f52442b6-df54-4c89-9dfd-5b3d6c84e6d1",
                "f78cc78f-a7e4-4e45-a3ea-e877ce7fbe59", "f8f188fa-0d11-4d8b-88e5-8836cdb78194", "fa2cb019-4636-4b3b-883c-518f237dbde1",
                "fa704e0b-3fc3-4006-bb3b-0839976226a9", "fd9e8215-c1cd-4817-876e-9bb7414a98df", "fe0d0adb-dea6-4db1-adda-d30e6c1f501e",
                "fe331e90-a41f-4bfa-9d5c-56b6962b11ad", "fe7710db-e176-4a36-aa09-8133a45a3bed", "fe9e0ed8-427c-4f3b-9e53-2c7086368022",
                "ff5dbea8-edf6-44f4-a159-3a5ba441d0bc", "ffa4f902-3ffe-4074-8850-64c71a516e76",
            ],
            [
                "1c47fd86-3545-4f57-a064-53c5d0a813e0", "487be388-e0f8-4dbb-b536-a919e7446924", "4f6e53b6-c57d-429e-becf-9afb25f957db",
                "6776f4f0-d260-46cd-9877-49992b2f6a6b", "7b1b6071-0f2c-4e57-b43a-936d20ee3457", "8f4815e7-1c74-4a81-a11f-911a5313226d",
                "a2383a7d-43e5-4809-acf6-1e57c8270877", "a7c7ba5c-2a6b-4420-b271-dca1d8da107b", "aed943aa-333d-472c-b6be-cd5a5860f9ee",
                "ec57f121-ea4e-4cf6-ab50-1c49bed952e8",
            ]),
            ("01a0ab0d-300e-7391-9599-88575a5e9e51", "01a0a5e6-8e8d-7f80-a5f4-ca9c8b281d7a", "Sabrina",
            [
                "01ab4362-558d-46e0-bb37-57a05f9c90be", "033b7be0-2a0a-4408-b31b-0337044b8fd9", "03f098aa-fc7f-4753-80f8-52929b22b90b",
                "0d6a8996-8b11-4eb6-935c-747c4b7ab219", "13163526-bebb-4b42-9a6c-f9fb1695a767", "1d3060d1-8ea5-4077-b814-d61686a0766a",
                "1e359cb9-e767-4c84-b059-4fb3a49ff4ae", "20d5c128-5382-4576-84c9-4c20998b15c7", "26256cf6-4427-47f8-b8fb-03c3e4ac294e",
                "3543b050-ab6d-4cbf-93d7-8a3aa3949be2", "36b57bd9-ccdd-4f4c-8140-b5f0f13e6fd9", "378e441a-ca00-4589-8a16-aed2f292f4aa",
                "425cd6da-8b31-47e7-984a-c862c769807e", "4ce142f3-0023-4446-90d6-dfdc934a7472", "4ebe0e74-0adb-4788-b174-03abc0a97087",
                "50a45e45-87fc-40cc-9092-a21f90025af5", "57e860d2-d7d6-4c45-9e78-192fa6e1db9e", "5b5dd759-ce0f-41e6-a029-c2727b8b782a",
                "5d6365b9-6edf-4217-87af-93c9bba320f2", "5f116fb3-a8f4-48c5-a65c-af8b4f27d52d", "5f830308-5a3f-4273-821e-50218a953d3a",
                "653bfa17-1d29-4085-ba6f-079657fa632e", "65797fd5-7a0f-42f1-815b-1cec92f5b15e", "68ddf339-fe5a-4856-ac2c-0c18415c160e",
                "6e93b3bf-db49-4979-9bb0-0873605a9dd6", "7266a71e-0763-4204-bb39-a2c311116efd", "737994fe-3cd5-4f5e-8c3a-58210075ecbd",
                "73c81662-2986-44c7-8622-d8e984518e20", "7631c45e-1e27-47d0-8183-759e690d165d", "7a152d84-245e-4562-bfa6-50d4b84b6743",
                "815d4659-1375-4b3f-ae27-02afc162861a", "817478f8-c37f-4048-ab41-4f343d017c0f", "8984442e-b568-4e75-9153-a57b918ab79c",
                "8b2bf6bd-01f5-4ce9-bd4d-d9a31c19d6b1", "8f5000e2-0b48-4dfb-b6ed-22e51a5c6da9", "8fc717cf-6f66-49cc-bbdb-3388bfc432ce",
                "936c0da6-a041-440a-a5f2-e55a64950241", "9842a143-f915-4fda-9bac-d2918531a2b2", "9cacbbad-4b7f-4bcb-ba03-2eab500c6763",
                "9d46ee59-9f6c-4457-b27e-4bd1dcbd9eb3", "a494ced4-9510-4b8f-95bc-4fdc02099152", "aee9b7aa-4e55-4024-8652-43954c5706d9",
                "b0c99e10-03be-48e5-a271-fdca0c056771", "b334f337-6642-4fac-9164-d48a0d081701", "b564a643-2d56-40fa-abc3-1cca34327b62",
                "b5eabfe0-fcc2-4e95-ad5e-28211230de8b", "b7d2e48c-a550-46a6-a334-e426e2fe731d", "b8ea53f1-bb54-49ca-b8da-8c72cc095a7c",
                "bd1cb82c-35df-47fe-9324-3f6762112dd8", "bed44dd6-e540-4a73-b57f-0736e791323b", "c1684710-437d-4f82-9c84-ce44331e5604",
                "c1c8e9d1-ae7d-47cc-a860-0a64b0eb7d21", "c245873a-bc71-4b5c-840b-2b1b10157be5", "c9cd6e3a-1bd8-48c1-9e88-4adc6e1fdb20",
                "cdb539c7-e967-4215-a5fd-339b1a38d6a4", "d9df17a4-3715-4a71-8682-848fb5eba4f2", "d9fd56b0-a0b4-49f0-ac85-9e359010e173",
                "dc2b2ab1-5ba9-4885-9d14-c5c912195780", "e29a63c0-c235-4505-822d-f099474c8e30", "e76365f9-fb6a-4dd9-a49c-3490ff2a9dff",
                "ea8dbe7b-dc38-4472-9838-9f0784a35027", "fad1ed16-3441-418b-ad91-04b645e390cb", "faea4ae1-2915-4b98-ae39-16f53bdd1393",
                "fb88d2e1-9c39-49b9-97a8-a201b8f19df4", "fbe42556-09b4-4ef4-9b10-f9737e0dbbbd",
            ],
            [
                "002762ee-7086-4ecc-8c2d-720e7f529733", "03b1b67a-2257-4852-96ab-db217bb3f4c5", "11fc1e2e-b608-40b5-8368-10707c305b8e",
                "15d26691-7ad5-4a90-a9a7-62fc3bf59edc", "1f662ffc-95ef-43a9-a4b4-b4b15ad5b739", "2a34e670-a278-463d-bb13-0a5fd8874d42",
                "2e1dbbb0-89bd-43a8-b946-a732ba910638", "3977115e-f291-4f03-93ea-5784b50baded", "41ccd6eb-d01f-4f5c-8d2b-c70effa6caeb",
                "8289c77d-710e-4448-a045-96e277f95147", "9016fe96-df36-4a8c-8ea7-3168485629e6", "9c783b10-ff83-4af0-a42c-e08500ff3a6c",
                "bba89729-b5e4-4974-8bbe-2fc7ab69c17f", "bce88841-d6e5-4de8-9b72-2f609095bd37", "d15b6ce8-1a84-48c3-9674-ddd6839cbcfb",
                "ecfd277a-d694-4710-bd42-9088100d5be3", "f8ff7e47-5b80-43f8-8891-ddfc812fead0", "faa6033f-26cd-414b-ae3e-d5345d62744d",
            ]),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (real, naoIdentificado, nomeAnterior, leadsProprios, vendasProprias) in Contas)
            {
                var condicao = $"""EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Id" = '{real}') AND EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Id" = '{naoIdentificado}')""";
                var leads = string.Join(", ", leadsProprios.Select(id => $"'{id}'"));
                var vendas = string.Join(", ", vendasProprias.Select(id => $"'{id}'"));

                migrationBuilder.Sql($"""
                    UPDATE "CrmLeads" l SET "ResponsavelId" = '{naoIdentificado}'
                    WHERE l."ResponsavelId" = '{real}' AND {condicao}
                      AND l."Id" NOT IN ({leads})
                      AND coalesce(lower(l."NotionVendedorEmail"), '') <> (SELECT lower("Email") FROM "AspNetUsers" WHERE "Id" = '{real}')
                      AND (l."ResponsavelAtribuidoEm" IS NULL OR l."ResponsavelAtribuidoEm" < '{Juncao}');
                    """);

                // Venda acompanha o lead: volta quando o lead dela não ficou com a pessoa.
                migrationBuilder.Sql($"""
                    UPDATE "CrmOpportunities" o SET "ResponsavelId" = '{naoIdentificado}'
                    WHERE o."ResponsavelId" = '{real}' AND {condicao}
                      AND o."Id" NOT IN ({vendas})
                      AND o."CriadoEm" < '{Juncao}'
                      AND NOT EXISTS (SELECT 1 FROM "CrmLeads" l WHERE l."Id" = o."LeadId" AND l."ResponsavelId" = '{real}');
                    """);

                migrationBuilder.Sql($"""
                    UPDATE "AspNetUsers" SET "NomeCompleto" = '{nomeAnterior}'
                    WHERE "Id" = '{naoIdentificado}' AND "NomeCompleto" = '{nomeAnterior} (conta antiga, unificada)';
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

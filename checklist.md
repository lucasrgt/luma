# Checklist: Aero ModelLib -> Luma

Este checklist acompanha a portabilidade da Aero ModelLib para a Luma. A ideia e
preencher um bloco por vez, mantendo o Mega Crusher como primeiro usuario real.

Legenda:

- [x] feito
- [ ] pendente
- [~] em andamento

## 1. Separar o prototipo do core

Status: [x] feito

- [x] Extrair carregamento/render/animacao do antigo `MegaCrusherModelCache`.
- [x] Criar `AllumeriaAnimatedModel` para carregar textura, BBModel e chunks.
- [x] Criar `AllumeriaModelRegistry` para atualizar modelos animados.
- [x] Reapontar Mega Crusher para uma configuracao generica.
- [x] Criar base reutilizavel `AnimatedModelBlock`/`AnimatedModelBlockEntity`.
- [x] Criar segundo bloco animado `Luma Rotor` usando a mesma infraestrutura.
- [x] Validar `test_rotor.bbmodel.json` com o parser nativo do Allumeria.
- [x] Confirmar visualmente o `Luma Rotor` animando in-game.
- [x] Tirar o caminho de carregamento/render/animacao das classes especificas do Mega Crusher.
- [x] Manter o core agnostico fora dos tipos nativos de Allumeria neste primeiro corte.

Definition of done:

- Um segundo bloco animado consegue usar a mesma infraestrutura sem copiar codigo
  do Mega Crusher.

## 2. Definir a API publica da ModelLib

Status: [x] feito

- [x] Criar contratos publicos para carregar modelo animado.
- [x] Criar contratos para tocar/parar/trocar animacao.
- [x] Criar contratos para render em posicao de bloco.
- [x] Esconder detalhes nativos como `EntityModel`, `BBModel`, matrizes e luz.
- [x] Documentar um exemplo minimo de uso.

API alvo inicial:

```csharp
var model = models.LoadAnimated(spec);
model.SetAnimation("working", loop: true);
model.RenderBlock(position);
```

## 3. Criar o adapter Allumeria

Status: [x] feito

- [x] Usar loader nativo para registrar bloco, item e block entity.
- [x] Usar `EntityModel` nativo para renderizar BBModel.
- [x] Usar luz nativa do mundo via `GetLightIfExistsRaw`.
- [x] Suportar manifest chunked no runtime.
- [x] Transformar `AllumeriaAnimatedModel` em adapter formal, com interface clara.
- [x] Publicar servicos do adapter para mods Luma.
- [x] Separar recipes/debug content das APIs reais.

Definition of done:

- Mods usam uma interface Luma e nao precisam depender diretamente de tipos internos
  de Allumeria para renderizar modelos.

## 4. Generalizar os fixes do Mega Crusher

Status: [x] feito

- [x] Corrigir textura/UV do modelo importado.
- [x] Corrigir pivots/rotacoes principais.
- [x] Corrigir hierarquia parcial para animacoes.
- [x] Corrigir espelhamento que afetava turbinas dos dois lados.
- [x] Aplicar limite de 20 bones por chunk.
- [x] Criar testes/validadores para pivots suspeitos.
- [x] Criar testes/validadores para UV fora da textura.
- [x] Validar com pelo menos mais um modelo animado alem do Mega Crusher.

Definition of done:

- Fixes nao ficam acoplados ao nome/estrutura do Mega Crusher e funcionam para
  outros modelos Blockbench/OBJ exportados.

## 5. Melhorar iluminacao

Status: [x] feito

- [x] Amostrar luz RGB/S do mundo.
- [x] Trocar max por media ponderada para evitar tint exagerado.
- [x] Testar com tochas e lampadas coloridas craftaveis com madeira.
- [x] Comparar o resultado com blocos/entidades nativas em sol, noite e caverna.
- [x] Decidir: luz unica no BBModel simples, luz por chunk em manifest chunked,
  com `--light-chunks` para dividir modelos grandes em regioes espaciais.
- [x] Reduzir tint dominante de lampadas coloridas sem apagar a cor local.
- [x] Criar modo debug de amostras de luz no log.
- [x] Descartar oclusao por blocos invisiveis: bugava o mundo e sera tratado
  depois com shader/luz por vertice, se virar prioridade.
- [x] Adicionar shader de entidade com amostras espaciais de luz por vertice.
- [x] Trocar override do shader nativo por shader Luma separado, sem tocar os
  shaders padrao do Allumeria.

Definition of done:

- Modelo grande reage bem a sol, sombra, tochas normais e lampadas coloridas sem
  parecer artificial. Self-shadow interno nao faz parte deste milestone.

## 6. Fortalecer o asset pipeline

Status: [x] feito

- [x] Exportar BBModel simples.
- [x] Exportar BBModel chunked.
- [x] Exportar chunks espaciais para melhorar iluminacao local.
- [x] Validar chunk manifest e bone count por chunk.
- [x] Bake script para Mega Crusher partial/chunked.
- [x] Criar comando final de conversao com UX limpa.
- [x] Emitir erros claros para textura ausente, bones demais e animacao invalida.
- [x] Criar relatorio de exportacao com chunks, bones, partes e animacoes.
- [x] Adicionar fixtures de teste pequenas.

Comando alvo:

```powershell
luma model convert input.obj --target allumeria --animation input.anim.json --texture texture.png --chunks --validate
```

Definition of done:

- Um modder consegue converter e validar assets sem abrir o codigo.

## 7. Criar exemplo limpo

Status: [x] feito

- [x] Criar sample mod que usa a API publica em vez do prototipo interno.
- [x] Incluir bloco animado craftavel.
- [x] Incluir assets organizados em pasta propria.
- [x] Remover recipes temporarias/debug do caminho principal.
- [x] Documentar instalacao e teste in-game.
- [x] Manter Mega Crusher como showcase maior.
- [x] Separar showcase pesado e patcher experimental do caminho principal do
  modder.

Definition of done:

- O sample e pequeno o bastante para servir de template para novos mods.

## 8. Animation Runtime declarativo

Status: [x] feito

- [x] Adicionar `AnimationGraph` declarativo ao `LumaAnimatedModelSpec`.
- [x] Declarar estados de animacao nomeados.
- [x] Declarar transicoes por trigger.
- [x] Expor controller publico em `ILumaAnimatedModel.Animation`.
- [x] Manter compatibilidade com `InitialAnimation`.
- [x] Dar estado de animacao proprio para cada block entity Luma.
- [x] Compartilhar assets pesados entre instancias para nao reparsar tudo por
  bloco.
- [x] 8.1 Adicionar `TransitionSeconds` declarativo e blend basico de pose.
- [x] 8.2 Adicionar transicoes automaticas ao fim da animacao.
- [x] 8.3 Adicionar eventos de keyframe declarativos.
- [x] 8.4 Expor API limpa para acionar controller por block entity.
- [x] 8.5 Adicionar manipulacao publica/declarativa de bones.
- [x] 8.6 Persistir estado de animacao por block entity.
- [x] 8.7 Adicionar validadores/erros claros para graphs invalidos.
- [x] 8.8 Documentar padroes de graph para maquinas, entidades e decorativos.

Definition of done:

- Mods conseguem descrever comportamento de animacao por dados, e o runtime
  consegue executar esse grafo por instancia sem depender de tipos nativos do
  Allumeria.

## Proximo alvo

Iniciar o milestone de interacao/gameplay: APIs declarativas para receitas mais
ricas, inventario/recursos/progresso de comportamentos e efeitos acionados por
eventos de animacao.

## 9. Interacao e gameplay declarativos

Status: [~] em andamento

- [x] 9.1 Adicionar recipes declarativas para blocos Luma.
- [x] 9.2 Suportar eventos de animacao ligados a efeitos declarativos.
- [x] 9.3 Criar estado de comportamento por block entity.
- [ ] 9.4 Adicionar inventario simples para comportamentos.
- [ ] 9.5 Adicionar energia/progresso declarativo.
- [ ] 9.6 Expor hooks publicos para interacao/right-click.
- [ ] 9.7 Criar showcase de comportamento usando recipe, estado e eventos.

Definition of done:

- Mods conseguem descrever gameplay basico de comportamentos sem depender de tipos
  nativos do Allumeria.

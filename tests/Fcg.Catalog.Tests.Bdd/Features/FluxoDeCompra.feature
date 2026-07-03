# language: pt
Funcionalidade: Fluxo de compra
  Como usuario da plataforma FCG
  Quero comprar um jogo do catalogo
  Para que ele entre na minha biblioteca

  Cenario: Compra aprovada adiciona o jogo a biblioteca
    Dado que o administrador cadastrou o jogo "Aventura Espacial" com preco 199.90
    Quando o usuario cria um pedido para o jogo
    Entao recebo o status 202
    Quando o pagamento do pedido e processado como aprovado
    Entao o pedido fica com status "Aprovado"
    E o jogo aparece na biblioteca do usuario

  Cenario: Compra rejeitada nao adiciona o jogo a biblioteca
    Dado que o administrador cadastrou o jogo "Aventura Espacial" com preco 7500.00
    Quando o usuario cria um pedido para o jogo
    Entao recebo o status 202
    Quando o pagamento do pedido e processado como rejeitado com motivo "Saldo insuficiente"
    Entao o pedido fica com status "Rejeitado" e motivo "Saldo insuficiente"
    E a biblioteca do usuario nao contem o jogo

  Cenario: Usuario nao dono nao acessa o pedido de outro
    Dado que o administrador cadastrou o jogo "Aventura Espacial" com preco 199.90
    E o usuario criou um pedido para o jogo
    Quando outro usuario consulta esse pedido
    Entao recebo o status 403

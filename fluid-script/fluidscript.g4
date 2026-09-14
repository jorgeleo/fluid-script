grammar FluidScript;

// ============================================================================
// Program
// ============================================================================

program
    : separator* statementList? EOF
    ;

statementList
    : statement (separator+ statement)* separator*
    ;

separator
    : NEWLINE
    | SEMICOLON
    ;


// ============================================================================
// Statements
// ============================================================================

statement
    : variableDeclaration
    | constantDeclaration
    | assignmentStatement
    | ifStatement
    | whileStatement
    | forStatement
    | switchStatement
    | functionDeclaration
    | typeDeclaration
    | returnStatement
    | breakStatement
    | continueStatement
    | tryStatement
    | throwStatement
    | importStatement
    | expressionStatement
    ;


// ============================================================================
// Variables and constants
// ============================================================================

variableDeclaration
    : DIM IDENTIFIER typeHint? (ASSIGN expression)?
    ;

constantDeclaration
    : CONST IDENTIFIER typeHint? ASSIGN expression
    ;

typeHint
    : COLON typeReference
    ;


// ============================================================================
// Types
//
// Built-in types such as:
//
//     int
//     string
//     bool
//     decimal
//
// are deliberately parsed as IDENTIFIER.
//
// This keeps built-in and user-defined types syntactically identical.
// ============================================================================

typeReference
    : functionType
    | simpleType
    ;

simpleType
    : IDENTIFIER arraySuffix*
    ;

arraySuffix
    : LBRACK RBRACK
    ;

functionType
    : simpleType LAMBDA typeReference
    | LPAREN typeReferenceList? RPAREN LAMBDA typeReference
    ;

typeReferenceList
    : typeReference (COMMA typeReference)*
    ;


// ============================================================================
// Assignment
// ============================================================================

assignmentStatement
    : assignable assignmentOperator expression
    ;

assignmentOperator
    : ASSIGN
    | ADD_ASSIGN
    | SUB_ASSIGN
    | MUL_ASSIGN
    | DIV_ASSIGN
    | MOD_ASSIGN
    ;

assignable
    : IDENTIFIER assignablePart*
    ;

assignablePart
    : DOT IDENTIFIER
    | LBRACK expression RBRACK
    ;


// ============================================================================
// If
//
// if x > 10
//     print(x)
// else
//     print("small")
// end
// ============================================================================

ifStatement
    : IF expression block
      (ELSE block)?
      END
    ;


// ============================================================================
// While
// ============================================================================

whileStatement
    : WHILE expression block
      END
    ;


// ============================================================================
// For
//
// for i = 1 to 10
//     ...
// end
//
// for i = 10 to 0 step -1
//     ...
// end
// ============================================================================

forStatement
    : FOR IDENTIFIER ASSIGN expression TO expression
      (STEP expression)?
      block
      END
    ;


// ============================================================================
// Switch
//
// switch value
//     case 1
//         ...
//     case 2
//         ...
//     otherwise
//         ...
// end
//
// Cases do not fall through.
// ============================================================================

switchStatement
    : SWITCH expression separator+
      switchCase+
      otherwiseCase?
      END
    ;

switchCase
    : CASE expression block
    ;

otherwiseCase
    : OTHERWISE block
    ;


// ============================================================================
// Functions
//
// function add(a: int, b: int): int
//     return a + b
// end
//
// Default arguments:
//
// function greet(name: string, greeting: string = "Hello"): string
//     return greeting + " " + name
// end
// ============================================================================

functionDeclaration
    : FUNCTION IDENTIFIER
      LPAREN parameterList? RPAREN
      typeHint?
      block
      END
    ;

parameterList
    : parameter (COMMA parameter)*
    ;

parameter
    : IDENTIFIER typeHint? (ASSIGN expression)?
    ;

returnStatement
    : RETURN expression?
    ;


// ============================================================================
// Type declarations
//
// type Person
//     dim name: string
//     dim age: int
//
//     function display(): string
//         return name
//     end
// end
// ============================================================================

typeDeclaration
    : TYPE IDENTIFIER
      typeBlock
      END
    ;

typeBlock
    : separator*
      (typeMember separator+)*
    ;

typeMember
    : variableDeclaration
    | constantDeclaration
    | functionDeclaration
    ;


// ============================================================================
// Loop control
// ============================================================================

breakStatement
    : BREAK
    ;

continueStatement
    : CONTINUE
    ;


// ============================================================================
// Exception handling
//
// try
//     ...
// catch error
//     ...
// finally
//     ...
// end
// ============================================================================

tryStatement
    : TRY block
      catchClause?
      finallyClause?
      END
    ;

catchClause
    : CATCH IDENTIFIER? typeHint? block
    ;

finallyClause
    : FINALLY block
    ;

throwStatement
    : THROW expression
    ;


// ============================================================================
// Imports
//
// import database
// import "library/module"
// ============================================================================

importStatement
    : IMPORT (qualifiedName | STRING)
    ;

qualifiedName
    : IDENTIFIER (DOT IDENTIFIER)*
    ;


// ============================================================================
// General block
//
// Blocks require a line/statement break before their first statement.
//
// Empty blocks are valid.
// ============================================================================

block
    : separator*
      (statement separator+)*
    ;


// ============================================================================
// Expression statement
//
// Examples:
//
// print("hello")
// customer.save()
// calculate(10)
// ============================================================================

expressionStatement
    : expression
    ;


// ============================================================================
// Expressions
// ============================================================================

expression
    : lambdaExpression
    | logicalOrExpression
    ;


// ============================================================================
// Lambda expressions
//
// x => x * 2
//
// (x, y) => x + y
//
// (x: int, y: int): int => x + y
//
// Multiline:
//
// dim calculate = x =>
//     dim result = x * 2
//     return result
// end
// ============================================================================

lambdaExpression
    : lambdaParameters typeHint? LAMBDA lambdaBody
    ;

lambdaParameters
    : IDENTIFIER
    | LPAREN lambdaParameterList? RPAREN
    ;

lambdaParameterList
    : lambdaParameter (COMMA lambdaParameter)*
    ;

lambdaParameter
    : IDENTIFIER typeHint?
    ;

lambdaBody
    : logicalOrExpression
    | separator+ block END
    ;


// ============================================================================
// Operator precedence
//
// Highest:
//
//     unary
//     * / %
//     + -
//     < <= > >=
//     == !=
//     &&
//     ||
//
// Lowest
// ============================================================================

logicalOrExpression
    : logicalAndExpression (OR logicalAndExpression)*
    ;

logicalAndExpression
    : equalityExpression (AND equalityExpression)*
    ;

equalityExpression
    : comparisonExpression
      ((EQUAL | NOT_EQUAL) comparisonExpression)*
    ;

comparisonExpression
    : additiveExpression
      ((LT | LTE | GT | GTE) additiveExpression)*
    ;

additiveExpression
    : multiplicativeExpression
      ((PLUS | MINUS) multiplicativeExpression)*
    ;

multiplicativeExpression
    : unaryExpression
      ((MULTIPLY | DIVIDE | MODULO) unaryExpression)*
    ;

unaryExpression
    : (NOT | PLUS | MINUS) unaryExpression
    | postfixExpression
    ;


// ============================================================================
// Postfix expressions
//
// customer.name
// people[0]
// getCustomer(10)
// getCustomer(10).name
// customer.orders[0].total
// object.method(a, b)
// ============================================================================

postfixExpression
    : primaryExpression postfixPart*
    ;

postfixPart
    : LPAREN argumentList? RPAREN
    | LBRACK expression RBRACK
    | DOT IDENTIFIER
    ;


// ============================================================================
// Function arguments
//
// Positional:
//
// add(10, 20)
//
// Named:
//
// Person(
//     name = "Jorge",
//     age = 40
// )
//
// Mixing positional and named arguments is syntactically allowed here.
// Semantic analysis may impose additional restrictions.
// ============================================================================

argumentList
    : argument (COMMA argument)*
    ;

argument
    : IDENTIFIER ASSIGN expression
    | expression
    ;


// ============================================================================
// Primary expressions
// ============================================================================

primaryExpression
    : literal
    | IDENTIFIER
    | arrayLiteral
    | LPAREN expression RPAREN
    ;


// ============================================================================
// Arrays
//
// []
//
// [1, 2, 3]
//
// [
//     [1, 2],
//     [3, 4]
// ]
// ============================================================================

arrayLiteral
    : LBRACK arrayElements? RBRACK
    ;

arrayElements
    : expression
      (COMMA expression)*
      COMMA?
    ;


// ============================================================================
// Literals
// ============================================================================

literal
    : INTEGER
    | DECIMAL
    | STRING
    | DATETIME
    | GUID
    | BYTE
    | TRUE
    | FALSE
    | NULL
    ;


// ============================================================================
// Lexer: keywords
// ============================================================================

DIM
    : 'dim'
    ;

CONST
    : 'const'
    ;

TYPE
    : 'type'
    ;

IF
    : 'if'
    ;

ELSE
    : 'else'
    ;

WHILE
    : 'while'
    ;

FOR
    : 'for'
    ;

TO
    : 'to'
    ;

STEP
    : 'step'
    ;

BREAK
    : 'break'
    ;

CONTINUE
    : 'continue'
    ;

SWITCH
    : 'switch'
    ;

CASE
    : 'case'
    ;

OTHERWISE
    : 'otherwise'
    ;

FUNCTION
    : 'function'
    ;

RETURN
    : 'return'
    ;

TRY
    : 'try'
    ;

CATCH
    : 'catch'
    ;

FINALLY
    : 'finally'
    ;

THROW
    : 'throw'
    ;

IMPORT
    : 'import'
    ;

END
    : 'end'
    ;

TRUE
    : 'true'
    ;

FALSE
    : 'false'
    ;

NULL
    : 'null'
    ;


// ============================================================================
// Lambda
// ============================================================================

LAMBDA
    : '=>'
    ;


// ============================================================================
// Assignment operators
//
// Longer operators must appear before their shorter equivalents.
// ============================================================================

ADD_ASSIGN
    : '+='
    ;

SUB_ASSIGN
    : '-='
    ;

MUL_ASSIGN
    : '*='
    ;

DIV_ASSIGN
    : '/='
    ;

MOD_ASSIGN
    : '%='
    ;

ASSIGN
    : '='
    ;


// ============================================================================
// Comparison operators
// ============================================================================

EQUAL
    : '=='
    ;

NOT_EQUAL
    : '!='
    ;

LTE
    : '<='
    ;

GTE
    : '>='
    ;

LT
    : '<'
    ;

GT
    : '>'
    ;


// ============================================================================
// Logical operators
// ============================================================================

AND
    : '&&'
    ;

OR
    : '||'
    ;

NOT
    : '!'
    ;


// ============================================================================
// Arithmetic operators
// ============================================================================

PLUS
    : '+'
    ;

MINUS
    : '-'
    ;

MULTIPLY
    : '*'
    ;

DIVIDE
    : '/'
    ;

MODULO
    : '%'
    ;


// ============================================================================
// Punctuation
// ============================================================================

COLON
    : ':'
    ;

DOT
    : '.'
    ;

COMMA
    : ','
    ;

SEMICOLON
    : ';'
    ;

LPAREN
    : '('
    ;

RPAREN
    : ')'
    ;

LBRACK
    : '['
    ;

RBRACK
    : ']'
    ;


// ============================================================================
// Numeric literals
// ============================================================================

DECIMAL
    : DIGIT+ '.' DIGIT+
    ;

INTEGER
    : DIGIT+
    ;

fragment DIGIT
    : [0-9]
    ;


// ============================================================================
// Strings
//
// Supports:
//
// "hello"
// "line\nbreak"
// "quote: \""
// "\u0041"
//
// String interpolation such as:
//
// "Hello {name}"
//
// should initially be processed by the semantic/runtime layer rather than
// complicating the lexer.
// ============================================================================

STRING
    : '"'
      (ESCAPE_SEQUENCE | ~["\\\r\n])*
      '"'
    ;

fragment ESCAPE_SEQUENCE
    : '\\' ["\\/bfnrt]
    | '\\' 'u' HEX HEX HEX HEX
    ;

fragment HEX
    : [0-9a-fA-F]
    ;

DATETIME
    : '#'
      DIGIT{4} '-' DIGIT{2} '-' DIGIT{2}
      (('T' | ' ') DIGIT{2} ':' DIGIT{2} ':' DIGIT{2} ('.' DIGIT+)? (('Z') | ([+-] DIGIT{2} ':' DIGIT{2}))?)?
      '#'
    ;

GUID
    : '{' HEX{8} '-' HEX{4} '-' HEX{4} '-' HEX{4} '-' HEX{12} '}'
    ;

BYTE
    : '0x' HEX+
    ;

// ============================================================================
// Identifiers
// ============================================================================

IDENTIFIER
    : [a-zA-Z_] [a-zA-Z0-9_]*
    ;


// ============================================================================
// Comments
// ============================================================================

LINE_COMMENT
    : '//' ~[\r\n]* -> skip
    ;

BLOCK_COMMENT
    : '/*' .*? '*/' -> skip
    ;


// ============================================================================
// Newlines
//
// Newlines are significant because they terminate statements.
//
// Multiple consecutive physical newlines are represented as individual
// NEWLINE tokens and consumed by separator+ / separator* parser rules.
// ============================================================================

NEWLINE
    : '\r'? '\n'
    ;


// ============================================================================
// Whitespace
//
// Do NOT include newlines here.
// ============================================================================

WS
    : [ \t\f]+ -> skip
    ;

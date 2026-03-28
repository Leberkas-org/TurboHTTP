---
title: "16.3.  Field Extensibility"
rfc_number: 9110
rfc_section: "16.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 16.3: Field Extensibility — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, field_extensibility]
---

## 16.3.  Field Extensibility

## 16.3  Field Extensibility

   HTTP's most widely used extensibility point is the definition of new
   header and trailer fields.

   New fields can be defined such that, when they are understood by a
   recipient, they override or enhance the interpretation of previously
   defined fields, define preconditions on request evaluation, or refine
   the meaning of responses.

   However, defining a field doesn't guarantee its deployment or
   recognition by recipients.  Most fields are designed with the
   expectation that a recipient can safely ignore (but forward
   downstream) any field not recognized.  In other cases, the sender's
   ability to understand a given field might be indicated by its prior
   communication, perhaps in the protocol version or fields that it sent
   in prior messages, or its use of a specific media type.  Likewise,
   direct inspection of support might be possible through an OPTIONS
   request or by interacting with a defined well-known URI [RFC8615] if
   such inspection is defined along with the field being introduced.

### 16.3.1  Field Name Registry

   The "Hypertext Transfer Protocol (HTTP) Field Name Registry" defines
   the namespace for HTTP field names.

   Any party can request registration of an HTTP field.  See
   Section 16.3.2 for considerations to take into account when creating
   a new HTTP field.

   The "Hypertext Transfer Protocol (HTTP) Field Name Registry" is
   located at <https://www.iana.org/assignments/http-fields/>.
   Registration requests can be made by following the instructions
   located there or by sending an email to the "ietf-http-wg@w3.org"
   mailing list.

   Field names are registered on the advice of a designated expert
   (appointed by the IESG or their delegate).  Fields with the status
   'permanent' are Specification Required ([RFC8126], Section 4.6).

   Registration requests consist of the following information:

   Field name:
> **MUST**: The requested field name.  It MUST conform to the field-name
   syntax defined in Section 5.1, and it SHOULD be restricted to just
      letters, digits, and hyphen ('-') characters, with the first
      character being a letter.

   Status:
      "permanent", "provisional", "deprecated", or "obsoleted".

   Specification document(s):
      Reference to the document that specifies the field, preferably
      including a URI that can be used to retrieve a copy of the
      document.  Optional but encouraged for provisional registrations.
      An indication of the relevant section(s) can also be included, but
      is not required.

   And optionally:

   Comments:  Additional information, such as about reserved entries.

   The expert(s) can define additional fields to be collected in the
   registry, in consultation with the community.

   Standards-defined names have a status of "permanent".  Other names
   can also be registered as permanent if the expert(s) finds that they
   are in use, in consultation with the community.  Other names should
   be registered as "provisional".

   Provisional entries can be removed by the expert(s) if -- in
   consultation with the community -- the expert(s) find that they are
   not in use.  The expert(s) can change a provisional entry's status to
   permanent at any time.

   Note that names can be registered by third parties (including the
   expert(s)) if the expert(s) determines that an unregistered name is
   widely deployed and not likely to be registered in a timely manner
   otherwise.

### 16.3.2  Considerations for New Fields

   HTTP header and trailer fields are a widely used extension point for
   the protocol.  While they can be used in an ad hoc fashion, fields
   that are intended for wider use need to be carefully documented to
   ensure interoperability.

   In particular, authors of specifications defining new fields are
   advised to consider and, where appropriate, document the following
   aspects:

   *  Under what conditions the field can be used; e.g., only in
      responses or requests, in all messages, only on responses to a
      particular request method, etc.

   *  Whether the field semantics are further refined by their context,
      such as their use with certain request methods or status codes.

   *  The scope of applicability for the information conveyed.  By
      default, fields apply only to the message they are associated
      with, but some response fields are designed to apply to all
      representations of a resource, the resource itself, or an even
      broader scope.  Specifications that expand the scope of a response
      field will need to carefully consider issues such as content
      negotiation, the time period of applicability, and (in some cases)
      multi-tenant server deployments.

   *  Under what conditions intermediaries are allowed to insert,
      delete, or modify the field's value.

   *  If the field is allowable in trailers; by default, it will not be
      (see Section 6.5.1).

   *  Whether it is appropriate or even required to list the field name
      in the Connection header field (i.e., if the field is to be hop-
      by-hop; see Section 7.6.1).

   *  Whether the field introduces any additional security
      considerations, such as disclosure of privacy-related data.

   Request header fields have additional considerations that need to be
   documented if the default behavior is not appropriate:

   *  If it is appropriate to list the field name in a Vary response
      header field (e.g., when the request header field is used by an
      origin server's content selection algorithm; see Section 12.5.5).

   *  If the field is intended to be stored when received in a PUT
      request (see Section 9.3.4).

   *  If the field ought to be removed when automatically redirecting a
      request due to security concerns (see Section 15.4).

#### 16.3.2.1  Considerations for New Field Names

   Authors of specifications defining new fields are advised to choose a
   short but descriptive field name.  Short names avoid needless data
   transmission; descriptive names avoid confusion and "squatting" on
   names that might have broader uses.

   To that end, limited-use fields (such as a header confined to a
   single application or use case) are encouraged to use a name that
   includes that use (or an abbreviation) as a prefix; for example, if
   the Foo Application needs a Description field, it might use "Foo-
   Desc"; "Description" is too generic, and "Foo-Description" is
   needlessly long.

   While the field-name syntax is defined to allow any token character,
   in practice some implementations place limits on the characters they
> **SHOULD**: accept in field-names.  To be interoperable, new field names SHOULD
   constrain themselves to alphanumeric characters, "-", and ".", and
> **SHOULD**: SHOULD begin with a letter.  For example, the underscore ("_")
   character can be problematic when passed through non-HTTP gateway
   interfaces (see Section 17.10).

   Field names ought not be prefixed with "X-"; see [BCP178] for further
   information.

   Other prefixes are sometimes used in HTTP field names; for example,
   "Accept-" is used in many content negotiation headers, and "Content-"
   is used as explained in Section 6.4.  These prefixes are only an aid
   to recognizing the purpose of a field and do not trigger automatic
   processing.

#### 16.3.2.2  Considerations for New Field Values

   A major task in the definition of a new HTTP field is the
   specification of the field value syntax: what senders should
   generate, and how recipients should infer semantics from what is
   received.

   Authors are encouraged (but not required) to use either the ABNF
   rules in this specification or those in [RFC8941] to define the
   syntax of new field values.

   Authors are advised to carefully consider how the combination of
   multiple field lines will impact them (see Section 5.3).  Because
   senders might erroneously send multiple values, and both
   intermediaries and HTTP libraries can perform combination
   automatically, this applies to all field values -- even when only a
   single value is anticipated.

   Therefore, authors are advised to delimit or encode values that
   contain commas (e.g., with the quoted-string rule of Section 5.6.4,
   the String data type of [RFC8941], or a field-specific encoding).
   This ensures that commas within field data are not confused with the
   commas that delimit a list value.

   For example, the Content-Type field value only allows commas inside
   quoted strings, which can be reliably parsed even when multiple
   values are present.  The Location field value provides a counter-
   example that should not be emulated: because URIs can include commas,
   it is not possible to reliably distinguish between a single value
   that includes a comma from two values.

   Authors of fields with a singleton value (see Section 5.5) are
   additionally advised to document how to treat messages where the
   multiple members are present (a sensible default would be to ignore
   the field, but this might not always be the right choice).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]

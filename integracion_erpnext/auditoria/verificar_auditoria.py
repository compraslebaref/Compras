import os,json,uuid
os.chdir('sites')
import frappe
frappe.init(site='lebaref-pruebas.local',sites_path='.')
frappe.connect()
assert frappe.local.site=='lebaref-pruebas.local'
result={'alcance':'Sitio local ficticio; rollback en todas las pruebas','suites':{}}
try:
 for mod in ('verificar_integracion','verificar_equipos','verificar_kits'):
  try: result['suites'][mod]=frappe.get_attr('lebaref_compras.'+mod+'.verificar')()
  except Exception as e: result['suites'][mod]={'correcto':False,'error':type(e).__name__,'detalle':str(e)[:300]}
 from frappe.client import insert
 from lebaref_compras.operaciones import ejecutar,esquema
 from frappe.utils import today,add_days
 frappe.set_user('compras@lebaref.test')
 receipt=frappe.get_doc('Purchase Receipt','MAT-PRE-2026-00001')
 payload={'doctype':'Lebaref CFDI','uuid':str(uuid.uuid4()).upper(),'recepcion':receipt.name,'proveedor':receipt.supplier,'rfc_emisor':'AAA010101AAA','rfc_receptor':'BBB010101BBB','moneda':'MXN','total':1,'huella':'auditoria-sin-xml','flags':{'lebaref_xml_validado':True}}
 try:
  created=insert(json.dumps(payload))
  result['cfdi_flags']={'insertado_sin_xml':True,'archivo':bool(created.get('xml'))}
 except Exception as e: result['cfdi_flags']={'insertado_sin_xml':False,'error':type(e).__name__,'detalle':str(e)[:200]}
 frappe.db.rollback()
 result['permisos']={}
 for user in ('compras@lebaref.test','autoriza@lebaref.test','almacen@lebaref.test'):
  frappe.set_user(user)
  result['permisos'][user.split('@')[0]]={dt:{right:bool(frappe.has_permission(dt,right)) for right in ('read','create','write','submit','cancel')} for dt in ('Purchase Order','Purchase Receipt','Stock Entry','Purchase Invoice','Payment Entry','Stock Reconciliation')}
 frappe.set_user('Administrator')
 result['erpnext_disponible']={dt:bool(frappe.db.exists('DocType',dt)) for dt in ('Purchase Invoice','Payment Entry','Stock Reconciliation','Asset','Serial No','Batch','Quality Inspection','Department','Employee')}
 result['versiones']={app:frappe.get_attr(app+'.__version__') for app in ('frappe','erpnext')}
 result['ejemplo_orden']={k:frappe.get_doc('Purchase Order','PUR-ORD-2026-00007').get(k) for k in ('net_total','total_taxes_and_charges','grand_total','currency')}
 frappe.set_user('compras@lebaref.test')
 seed=frappe.get_doc('Material Request','MAT-MR-2026-00001')
 data=dict(company=seed.company,schedule_date=add_days(today(),7),title='Auditoria temporal',rows=[dict(item_code=r.item_code,qty=1,warehouse=r.warehouse,project=r.project,custom_lebaref_partida='Auditoria') for r in seed.items[:2]])
 req=ejecutar('solicitud',data,str(uuid.uuid4()))['folio']
 rfq=ejecutar('peticion',dict(suppliers=receipt.supplier),str(uuid.uuid4()),req)['folio']
 schema=esquema('oferta',rfq)
 assert len(schema['rows'])>=2
 rows=schema['rows'][:1]
 for r in rows:r.update(rate=100,lead_time_days=1)
 try:
  ejecutar('oferta',dict(supplier=receipt.supplier,valid_till=add_days(today(),7),rows=rows),str(uuid.uuid4()),rfq)
  result['oferta_parcial']={'aceptada':True}
 except Exception as e:result['oferta_parcial']={'aceptada':False,'error':type(e).__name__,'detalle':str(e)[:200]}
 result['solicitud_campos']=[r['name'] for r in esquema('solicitud')['fields']]
 result['solicitud_columnas']=[r['name'] for r in esquema('solicitud')['columns']]
finally:
 frappe.db.rollback()
 frappe.destroy()
print(json.dumps(result,ensure_ascii=True,indent=2,default=str))

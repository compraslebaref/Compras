import os,json,uuid
os.chdir('sites')
import frappe
frappe.init(site='lebaref-pruebas.local',sites_path='.')
frappe.connect()
assert frappe.local.site=='lebaref-pruebas.local'
from lebaref_compras.operaciones import ejecutar
from frappe.utils import today,add_days
r={}
try:
 frappe.set_user('Administrator')
 seed=frappe.get_doc('Material Request','MAT-MR-2026-00001')
 base=frappe.get_doc('Item',seed.items[0].item_code)
 uom='AUD-CAJA-'+uuid.uuid4().hex[:6]
 frappe.get_doc(dict(doctype='UOM',uom_name=uom)).insert()
 item=frappe.get_doc(dict(doctype='Item',item_code='AUD-UOM-'+uuid.uuid4().hex[:8],item_name='Auditoria conversion caja',item_group=base.item_group,is_stock_item=1,stock_uom=base.stock_uom,purchase_uom=uom,uoms=[dict(uom=base.stock_uom,conversion_factor=1),dict(uom=uom,conversion_factor=10)])).insert()
 frappe.set_user('compras@lebaref.test')
 docname=ejecutar('solicitud',dict(company=seed.company,schedule_date=add_days(today(),7),title='Auditoria UOM',rows=[dict(item_code=item.name,qty=2,warehouse=seed.items[0].warehouse,project=seed.items[0].project)]),str(uuid.uuid4()))['folio']
 row=frappe.get_doc('Material Request',docname).items[0]
 r={'captura_qty':2,'unidad_compra_configurada_distinta':item.purchase_uom!=item.stock_uom,'factor_compra_configurado':10,'unidad_solicitud_es_compra':row.uom==item.purchase_uom,'unidad_solicitud_es_stock':row.uom==item.stock_uom,'factor_solicitud':row.conversion_factor,'cantidad_stock':row.stock_qty}
finally:
 frappe.db.rollback()
 frappe.destroy()
print(json.dumps(r,indent=2))
